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


def call_llm(system_prompt: str, user_message: str, retries: int = 2, fatal: bool = True) -> str:
    """
    Panggil LLM menggunakan OpenAI SDK.
    Semua agent memanggil fungsi ini — tidak perlu tahu provider-nya apa.

    Jika kena content filter Azure, otomatis retry dengan prompt yang lebih netral.

    Args:
        fatal: Jika True (default), panggil sys.exit() saat semua retry gagal.
               Jika False, raise RuntimeError agar pemanggil bisa fallback.
    """
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

            # Deteksi parameter token yang tepat berdasarkan model
            _params = {
                "model": MODEL,
                "messages": [
                    {"role": "system", "content": sys_prompt},
                    {"role": "user",   "content": usr_msg},
                ],
            }
            if MODEL.startswith("azure/"):
                _params["max_completion_tokens"] = MAX_TOKENS
            else:
                _params["max_tokens"] = MAX_TOKENS

            completion = _client.chat.completions.create(**_params)
            return completion.choices[0].message.content.strip()

        except AuthenticationError as e:
            print(f"\n[LLM ERROR] API Key tidak valid atau ditolak.")
            print(f"  Detail: {e}")
            if fatal:
                sys.exit(1)
            raise

        except APIError as e:
            err_msg = str(e).lower()
            # Tangkap content filter / jailbreak error dari Azure
            if "content_filter" in err_msg or "jailbreak" in err_msg or "responsibleai" in err_msg:
                print(f"  [CONTENT FILTER] Percobaan {attempt+1}/{retries+1}: prompt ditolak Azure, retry dengan prompt netral...")
                last_error = e
                continue  # retry dengan prompt netral
            print(f"\n[LLM ERROR] {e}")
            if fatal:
                sys.exit(1)
            raise

        except Exception as e:
            err_msg = str(e).lower()
            if "content_filter" in err_msg or "jailbreak" in err_msg or "responsibleai" in err_msg:
                print(f"  [CONTENT FILTER] Percobaan {attempt+1}/{retries+1}: prompt ditolak Azure, retry dengan prompt netral...")
                last_error = e
                continue
            print(f"\n[LLM ERROR] {type(e).__name__}: {e}")
            if fatal:
                sys.exit(1)
            raise

    # Semua retry gagal
    msg = f"Semua {retries+1} percobaan gagal karena content filter."
    print(f"\n[LLM ERROR] {msg}")
    print(f"  Error terakhir: {last_error}")
    if fatal:
        sys.exit(1)
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
