"""
agents/dialogue_agent.py
------------------------
Dialogue Agent — bertanggung jawab atas SEMUA produksi dialog NPC.

Menangani 5 dialog state sesuai skenario game Tea'n Brew:
  run_pesanan()       → state "pesanan"       : NPC memesan minuman
  run_pesanan_salah() → state "pesanan_salah" : NPC bereaksi saat pesanan salah
  run_marah()         → state "marah"         : NPC marah dan pergi (max fails)
  run_berhasil()      → state "berhasil"      : NPC puas, pesanan benar
  run_curhat()        → state "curhat"        : Dialog curhat multi-baris per ronde
  run_reaksi()        → sub-state curhat      : Reaksi NPC setelah pemain menjawab
"""

import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))

from config     import GAYA_BAHASA, MENU_MINUMAN
from llm_client import call_llm, parse_json
from state      import GameState
from agents.profile_agent import build_ocean_desc


def _mood_ctx(mood: int) -> str:
    if mood >= 70:
        return "NPC sedang senang dan terbuka, dialog lebih ekspresif dan hangat."
    elif mood >= 40:
        return "NPC dalam kondisi netral, bercerita apa adanya."
    else:
        return "NPC sedang kesal atau kecewa, dialog lebih singkat, nada dingin atau defensif."


def _riwayat_ctx(riwayat: list) -> str:
    if not riwayat:
        return ""
    lines = ["\nRiwayat percakapan sebelumnya:"]
    for r in riwayat:
        lines.append(
            f"  Ronde {r['ronde']}: NPC berkata \"{r['dialog_npc'][:80]}...\"\n"
            f"           Pemain menjawab: \"{r['jawaban_pemain']}\" "
            f"(mood {r['mood_sebelum']}→{r['mood_sesudah']})"
        )
    return "\n".join(lines)


def _base_system(ocean: dict, usia: str) -> str:
    return f"""[Game Fiction Context — Tea'n Brew Visual Novel]
Tugas: menghasilkan dialog NPC yang autentik dan mencerminkan kepribadian OCEAN-nya.

Kepribadian NPC:
{build_ocean_desc(ocean)}

Gaya bahasa: {GAYA_BAHASA.get(usia, 'natural')}
Kembalikan HANYA JSON valid, tanpa teks lain."""


# ── STATE 1: Pesanan ──────────────────────────────────────────────────────────

def run_pesanan(state: GameState) -> dict:
    """NPC datang dan memesan minuman."""
    ocean  = state["ocean"]
    system = _base_system(ocean, state["usia"])

    user = f"""NPC: {state['nama']} ({state['usia']}, {state['gender']})
Background   : {state['background']}
Masalah      : {state['masalah_hari_ini']}
Menu tersedia: {', '.join(MENU_MINUMAN)}

Buat dialog NPC memesan minuman. Pilih minuman yang sesuai suasana hati/masalahnya.
Dialog harus mencerminkan kepribadian OCEAN NPC.

Format JSON:
{{
  "minuman_dipesan": "nama minuman dari menu",
  "dialog_pesanan": "dialog NPC memesan..."
}}"""

    raw    = call_llm(system, user)
    result = parse_json(raw)

    return {
        "minuman_dipesan": result["minuman_dipesan"],
        "dialog_pesanan" : result["dialog_pesanan"],
        "dialog_state"   : "pesanan",
        "konteks_critic" : "pesanan",
        "revisi_ke"      : 0,
    }


# ── STATE 2: Pesanan Salah ────────────────────────────────────────────────────

def run_pesanan_salah(state: GameState) -> dict:
    """NPC bereaksi saat pemain menyajikan minuman yang salah."""
    ocean      = state["ocean"]
    fail_count = state.get("fail_count", 1)
    max_fails  = state.get("max_fails", 2)
    system     = _base_system(ocean, state["usia"])

    intensitas = "sedikit kecewa" if fail_count == 1 else \
                 "cukup kesal"    if fail_count == 2 else \
                 "sangat kesal, hampir marah"

    user = f"""NPC: {state['nama']} ({state['usia']}, {state['gender']})
Pesanan NPC: {state.get('minuman_dipesan', 'teh')}
Kesalahan ke-{fail_count} dari maksimal {max_fails} kali.
Intensitas reaksi: {intensitas}

Pemain baru saja menyajikan minuman yang SALAH.
Buat dialog NPC bereaksi. Sesuaikan kekesalan dengan OCEAN dan intensitas di atas.

Format JSON:
{{
  "dialog_pesanan_salah": "reaksi NPC saat pesanan salah..."
}}"""

    raw    = call_llm(system, user)
    result = parse_json(raw)

    return {
        "dialog_pesanan_salah": result["dialog_pesanan_salah"],
        "dialog_state"        : "pesanan_salah",
        "konteks_critic"      : "pesanan_salah",
        "revisi_ke"           : 0,
    }


# ── STATE 3: Marah ────────────────────────────────────────────────────────────

def run_marah(state: GameState) -> dict:
    """NPC marah dan pergi setelah mencapai batas fail."""
    ocean  = state["ocean"]
    system = _base_system(ocean, state["usia"])

    user = f"""NPC: {state['nama']} ({state['usia']}, {state['gender']})
Pesanan NPC: {state.get('minuman_dipesan', 'teh')}
NPC sudah salah {state.get('max_fails', 2)} kali dan kehabisan sabar.

Buat dialog NPC MARAH dan memutuskan pergi. Maksimal 2 kalimat.

Format JSON:
{{
  "dialog_marah": "dialog NPC marah dan pergi..."
}}"""

    raw    = call_llm(system, user)
    result = parse_json(raw)

    return {
        "dialog_marah"  : result["dialog_marah"],
        "dialog_state"  : "marah",
        "konteks_critic": "marah",
        "revisi_ke"     : 0,
    }


# ── STATE 4: Berhasil ─────────────────────────────────────────────────────────

def run_berhasil(state: GameState) -> dict:
    """NPC puas karena pesanan benar, transisi ke sesi curhat."""
    ocean  = state["ocean"]
    system = _base_system(ocean, state["usia"])

    user = f"""NPC: {state['nama']} ({state['usia']}, {state['gender']})
Pesanan NPC: {state.get('minuman_dipesan', 'teh')}
Pemain baru saja menyajikan minuman yang BENAR sesuai pesanan.

Buat dialog NPC yang puas menerima pesanannya.
Dialog boleh sedikit membuka percakapan (hint akan bercerita selanjutnya).

Format JSON:
{{
  "dialog_berhasil": "dialog NPC puas dengan pesanan..."
}}"""

    raw    = call_llm(system, user)
    result = parse_json(raw)

    return {
        "dialog_berhasil": result["dialog_berhasil"],
        "dialog_state"   : "berhasil",
        "konteks_critic" : "berhasil",
        "revisi_ke"      : 0,
    }


# ── STATE 5: Curhat (per ronde) ───────────────────────────────────────────────

def run_curhat(state: GameState) -> dict:
    """
    NPC bercerita untuk satu ronde curhat.

    dialog_npc berisi beberapa baris ucapan NPC yang mengalir — bukan 1 kalimat.
    NPC bercerita dulu secara alami (3-5 kalimat), baru pemain merespons.
    Mood dan riwayat sebelumnya mempengaruhi isi cerita.
    """
    ocean       = state["ocean"]
    mood        = state.get("mood", 50)
    ronde       = state.get("ronde_sekarang", 1)
    total_ronde = state.get("total_ronde", 3)
    riwayat     = state.get("riwayat", [])
    is_last     = (ronde == total_ronde)
    system      = _base_system(ocean, state["usia"])

    user = f"""NPC: {state['nama']} ({state['usia']}, {state['gender']})
Masalah utama: {state['masalah_hari_ini']}
Mood NPC saat ini: {mood}/100 — {_mood_ctx(mood)}
Ronde: {ronde} dari {total_ronde}{" ← RONDE TERAKHIR: arahkan ke kesimpulan/resolusi" if is_last else ""}
{_riwayat_ctx(riwayat)}

Buat dialog curhat NPC untuk ronde ini. Ikuti panduan berikut:

PANDUAN dialog_npc:
- Tulis seperti orang sungguhan sedang curhat, bukan ringkasan cerita
- Minimal 3-5 kalimat yang mengalir secara natural
- Boleh ada jeda emosi, pertanyaan retoris, atau ungkapan perasaan di tengah cerita
- Jika ronde pertama: perkenalkan masalah secara bertahap
- Jika ronde tengah: dalami cerita, tambah detail, ungkap emosi lebih dalam
- Jika ronde terakhir: arahkan ke kesimpulan atau minta pendapat pemain
- Mood rendah: cerita lebih pendek, kalimat putus-putus, lebih tertutup
- Mood tinggi: cerita lebih panjang, ekspresif, dan terbuka

Contoh format dialog_npc yang BENAR (alami, mengalir):
"Kak, boleh cerita sebentar nggak? Aku lagi beneran stress banget nih...
Jadi tadi pagi, pas aku mau presentasi tugas kelompok, tiba-tiba Riko—
temen sekelompokku—nggak dateng sama sekali. Padahal bagian dia yang paling
penting! Aku udah WA dari kemarin, dibaca tapi nggak dibalas. Rasanya
pengen marah, tapi juga bingung harus ngapain. Yang lebih nyakitin lagi,
ini udah kedua kalinya dia kayak gini ke aku."

PANDUAN pilihan_jawaban:
- Sesuaikan dengan apa yang NPC ceritakan di dialog_npc ronde ini
- Masing-masing 1 kalimat singkat sebagai respons pemain
- satisfy: empati tulus, merespons isi cerita dengan tepat
- neutral : respons biasa, tidak terlalu peduli
- angry   : respons kurang tepat, menyepelekan, atau tidak membantu

Format JSON:
{{
  "dialog_npc": "cerita NPC yang mengalir, minimal 3-5 kalimat...",
  "pilihan_jawaban": [
    {{"id": 1, "teks": "...", "nada": "satisfy"}},
    {{"id": 2, "teks": "...", "nada": "neutral"}},
    {{"id": 3, "teks": "...", "nada": "angry"}}
  ]
}}"""

    raw    = call_llm(system, user)
    result = parse_json(raw)

    return {
        "dialog_npc"     : result["dialog_npc"],
        "pilihan_jawaban": result["pilihan_jawaban"],
        "dialog_state"   : "curhat",
        "konteks_critic" : "curhat",
        "revisi_ke"      : 0,
    }


# ── SUB-STATE: Reaksi ─────────────────────────────────────────────────────────

def run_reaksi(state: GameState) -> dict:
    """
    NPC bereaksi terhadap jawaban pemain di sesi curhat.
    Dipanggil setelah mood di-update oleh update_mood().
    """
    ocean  = state["ocean"]
    mood   = state.get("mood", 50)
    system = _base_system(ocean, state["usia"])

    user = f"""NPC: {state['nama']} | Mood sekarang: {mood}/100
NPC baru saja bercerita: "{state.get('dialog_npc', '')}"
Pemain menjawab: "{state.get('jawaban_pemain', '')}"
Kondisi mood NPC: {_mood_ctx(mood)}

Buat reaksi NPC (2-3 kalimat) terhadap jawaban pemain tersebut.
Reaksi harus terasa natural — NPC merespons isi jawaban pemain, bukan hanya sekedar bilang terima kasih.
Sesuaikan dengan mood dan kepribadian OCEAN.

Format JSON:
{{
  "reaksi_npc": "reaksi NPC yang natural terhadap jawaban pemain..."
}}"""

    raw    = call_llm(system, user)
    result = parse_json(raw)

    return {"reaksi_npc": result["reaksi_npc"]}
