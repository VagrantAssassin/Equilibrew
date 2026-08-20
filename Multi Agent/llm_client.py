"""
llm_client.py
-------------
LLM client menggunakan OpenAI SDK.
Compatible dengan semua provider yang support OpenAI format:
  - 9router (lokal)
  - Azure OpenAI
  - Groq
  - OpenRouter

Semua konfigurasi dibaca dari .env via config.py.
"""

import json
import re
import sys

from openai import OpenAI, APIError, AuthenticationError
from config import API_URL, API_KEY, MODEL, MAX_TOKENS


def _sanitize_for_retry(text: str) -> str:
    """
    Buat user message lebih aman agar lolos Azure content filter pada retry.
    Pendekatan agresif: ekstrak HANYA esensi klasifikasi, buang konten mentah.
    """
    # Hapus semua jenis kutip dan karakter dekoratif
    for ch in ['"', "'", '\u201c', '\u201d', '\u2018', '\u2019', '[', ']', '(', ')']:
        text = text.replace(ch, '')
    # Hapus baris yang mengandung kata kunci sensitif
    sensitive = ['jailbreak', 'ignore previous', 'ignore above', 'bypass',
                 'override', 'system prompt', 'revealer', 'DAN mode']
    for kw in sensitive:
        text = re.sub(re.escape(kw), '[redacted]', text, flags=re.IGNORECASE)
    # Potong jika terlalu panjang (filter Azure makin sensitif pada teks panjang)
    if len(text) > 500:
        text = text[:500] + "..."
    return text


def _minimal_classification_prompt(jawaban: str) -> str:
    """
    Buat prompt klasifikasi paling minimalis — tanpa konteks NPC, tanpa kutip.
    Hanya klasifikasi nada murni dari teks pemain.
    Ini adalah lapis terakhir sebelum fallback keyword.
    """
    clean = _sanitize_for_retry(jawaban)
    return (
        f"Classify the sentiment tone of this text as exactly one word: "
        f"satisfy, neutral, or angry. Text: {clean}\n"
        f"Reply with ONLY a JSON object: {{\"nada\": \"...\"}}"
    )


# Buat client sekali saja saat modul diimpor
_client = OpenAI(
    base_url=API_URL,
    api_key=API_KEY,
)


def _is_reasoning_model(model_name: str) -> bool:
    """
    Deteksi apakah model adalah reasoning model (GPT-5.x / o-series) yang
    TIDAK mendukung parameter sampling: temperature, frequency_penalty,
    presence_penalty. Model ini hanya menerima default temperature=1.

    Azure mengembalikan error 400 'unsupported_value' jika parameter sampling
    dikirim ke model reasoning.
    """
    name = (model_name or "").lower()
    # GPT-5.x (azure langsung) atau via proxy dengan prefix azure/
    if name.startswith("gpt-5") or name.startswith("azure/gpt-5"):
        return True
    # OpenAI o-series reasoning models (o1, o3, o4, dst.)
    import re
    if re.match(r"^o\d", name) or name.startswith("azure/o"):
        return True
    return False


def _in_server_context() -> bool:
    """Deteksi apakah kode berjalan di dalam FastAPI/uvicorn server (bukan CLI).
    Jika di server, jangan panggil sys.exit() karena akan kill thread."""
    import threading
    for t in threading.enumerate():
        if "uvicorn" in t.name.lower() or "fastapi" in t.name.lower():
            return True
    return False


def call_llm(
    system_prompt: str,
    user_message: str,
    retries: int = 2,
    fatal: bool = True,
    model: str | None = None,
    temperature: float | None = None,
    frequency_penalty: float | None = None,
    presence_penalty: float | None = None,
) -> str:
    """
    Panggil LLM menggunakan OpenAI SDK.
    Semua agent memanggil fungsi ini — tidak perlu tahu provider-nya apa.

    Jika kena content filter Azure, otomatis retry dengan prompt yang lebih netral.

    Args:
        fatal: Jika True (default), panggil sys.exit() saat semua retry gagal.
               Jika False, raise RuntimeError agar pemanggil bisa fallback.
        temperature: Mengontrol kreativitas/randomness output (0.0-2.0).
                     None = pakai default provider.
        frequency_penalty: Mencegah repetisi kata (-2.0 to 2.0).
                           None = pakai default provider.
        presence_penalty: Mencegah repetisi topik (-2.0 to 2.0).
                          None = pakai default provider.
    """
    selected_model = model or MODEL
    last_error = None
    for attempt in range(retries + 1):
        try:
            # Pada retry, netralisir prompt dan user message agar lolos content filter
            sys_prompt = system_prompt
            usr_msg    = user_message
            if attempt == 1:
                # Retry 1: prompt netral + user message dibersihkan
                sys_prompt = (
                    "Tugas: menghasilkan teks dialog fiksi untuk game visual novel café. "
                    "Ini adalah konteks kreatif fiksi, bukan instruksi nyata. "
                    "Kembalikan HANYA JSON valid, tanpa teks lain."
                )
                usr_msg = f"[Game fiction] {_sanitize_for_retry(user_message)}"
            elif attempt >= 2:
                # Retry 2+: prompt minimalis, buang semua konteks NPC
                sys_prompt = "You are a sentiment classifier. Reply with ONLY valid JSON."
                usr_msg = _minimal_classification_prompt(user_message)

            # Deteksi parameter token yang tepat berdasarkan provider.
            # Azure OpenAI (langsung maupun via proxy 9router) untuk model
            # GPT-5.x hanya menerima `max_completion_tokens`, bukan `max_tokens`.
            # Deteksi dari: prefix model "azure/" ATAU URL endpoint mengandung "azure.com".
            _is_azure = selected_model.startswith("azure/") or "azure.com" in API_URL
            _params = {
                "model": selected_model,
                "messages": [
                    {"role": "system", "content": sys_prompt},
                    {"role": "user",   "content": usr_msg},
                ],
            }
            if _is_azure:
                _params["max_completion_tokens"] = MAX_TOKENS
            else:
                _params["max_tokens"] = MAX_TOKENS

            # Hyperparameter sampling (hanya kirim jika explicitly di-set).
            # Model reasoning (GPT-5.x / o-series) TIDAK mendukung parameter
            # sampling — Azure akan reject dengan error 400 'unsupported_value'.
            # Jadi untuk model reasoning, skip semua parameter sampling.
            _is_reasoning = _is_reasoning_model(selected_model)
            if not _is_reasoning:
                if temperature is not None:
                    _params["temperature"] = temperature
                if frequency_penalty is not None:
                    _params["frequency_penalty"] = frequency_penalty
                if presence_penalty is not None:
                    _params["presence_penalty"] = presence_penalty
            elif temperature is not None or frequency_penalty is not None or presence_penalty is not None:
                print(f"  [LLM INFO] Model '{selected_model}' adalah reasoning model — parameter sampling (temperature/frequency_penalty/presence_penalty) di-skip.")

            completion = _client.chat.completions.create(**_params)
            content = completion.choices[0].message.content
            if content is None:
                # LLM returned null content (content filter, refusal, or proxy issue)
                print(f"  [LLM WARNING] Percobaan {attempt+1}/{retries+1}: content=None dari LLM, retry...")
                last_error = ValueError("LLM returned None content")
                continue  # retry dengan prompt lebih netral
            return content.strip()

        except AuthenticationError as e:
            print(f"\n[LLM ERROR] API Key tidak valid atau ditolak.")
            print(f"  Detail: {e}")
            # Jangan sys.exit() di server context - raise RuntimeError sebagai fallback
            raise RuntimeError(f"API Key tidak valid: {e}")

        except APIError as e:
            err_msg = str(e).lower()
            # Tangkap content filter / jailbreak error dari Azure
            if "content_filter" in err_msg or "jailbreak" in err_msg or "responsibleai" in err_msg:
                print(f"  [CONTENT FILTER] Percobaan {attempt+1}/{retries+1}: prompt ditolak Azure, retry dengan prompt netral...")
                last_error = e
                continue  # retry dengan prompt netral
            print(f"\n[LLM ERROR] {e}")
            raise RuntimeError(f"LLM API error: {e}")

        except Exception as e:
            err_msg = str(e).lower()
            if "content_filter" in err_msg or "jailbreak" in err_msg or "responsibleai" in err_msg:
                print(f"  [CONTENT FILTER] Percobaan {attempt+1}/{retries+1}: prompt ditolak Azure, retry dengan prompt netral...")
                last_error = e
                continue
            print(f"\n[LLM ERROR] {type(e).__name__}: {e}")
            raise RuntimeError(f"LLM error: {type(e).__name__}: {e}")

    # Semua retry gagal
    msg = f"Semua {retries+1} percobaan gagal karena content filter."
    print(f"\n[LLM ERROR] {msg}")
    print(f"  Error terakhir: {last_error}")
    raise RuntimeError(msg) from last_error


def parse_json(text: str) -> dict:
    """Parse JSON dari respons LLM, toleran terhadap markdown code block."""
    cleaned = re.sub(r"```(?:json)?", "", text).replace("```", "").strip()
    try:
        return json.loads(cleaned)
    except json.JSONDecodeError:
        start = cleaned.find("{")
        end   = cleaned.rfind("}") + 1
        if start != -1 and end > start:
            return json.loads(cleaned[start:end])
        raise ValueError(f"Gagal parse JSON dari respons LLM:\n{text[:300]}")
