"""
agents/critic_agent.py
----------------------
Critic Agent — quality gate dalam pola Actor-Critic.

Memvalidasi dialog dari Dialogue Agent terhadap:
  1. Konsistensi kepribadian OCEAN
  2. Kesesuaian gaya bahasa usia & gender
  3. Kesesuaian emosi dengan dialog state aktif

Lulus jika skor >= CRITIC_THRESHOLD (default 70).
Jika gagal → run_revisi() dipanggil (maks MAX_REVISI kali).
"""

import json
import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))

from config     import GAYA_BAHASA, CRITIC_THRESHOLD
from llm_client import call_llm, parse_json
from state      import GameState


# Panduan emosi yang diharapkan per dialog state
EMOSI_EXPECTED = {
    "pesanan"       : "santai, mungkin sedikit gelisah karena membawa masalah",
    "pesanan_salah" : "kecewa atau kesal, intensitas sesuai jumlah kesalahan",
    "marah"         : "marah dan frustrasi, memutuskan pergi",
    "berhasil"      : "lega dan senang, mungkin sedikit lebih terbuka",
    "curhat"        : "emosional sesuai masalah dan mood saat ini",
}


def run(state: GameState) -> dict:
    """
    Node LangGraph: critic_agent
    Menentukan dialog yang divalidasi berdasarkan konteks_critic di state.
    """
    ocean    = state["ocean"]
    konteks  = state.get("konteks_critic", "pesanan")

    # Pilih field dialog yang relevan untuk divalidasi
    dialog_map = {
        "pesanan"       : {"dialog_pesanan": state.get("dialog_pesanan", ""),
                           "minuman_dipesan": state.get("minuman_dipesan", "")},
        "pesanan_salah" : {"dialog_pesanan_salah": state.get("dialog_pesanan_salah", "")},
        "marah"         : {"dialog_marah": state.get("dialog_marah", "")},
        "berhasil"      : {"dialog_berhasil": state.get("dialog_berhasil", "")},
        "curhat"        : {"dialog_npc": state.get("dialog_npc", ""),
                           "pilihan_jawaban": state.get("pilihan_jawaban", [])},
    }
    dialog_target = dialog_map.get(konteks, {})

    system = """[Game Fiction Context — Tea'n Brew Visual Novel]
Tugas: memvalidasi dialog NPC terhadap profil OCEAN, gaya bahasa usia/gender,
dan emosi yang seharusnya muncul pada dialog state tersebut.
Kembalikan HANYA JSON valid, tanpa teks lain."""

    user = f"""Validasi dialog NPC (state: {konteks}):

=== PROFIL NPC ===
Nama  : {state.get('nama', '')} | Usia: {state.get('usia', '')} | Gender: {state.get('gender', '')}
OCEAN :
  Openness         : {ocean['openness']}/100
  Conscientiousness: {ocean['conscientiousness']}/100
  Extraversion     : {ocean['extraversion']}/100
  Agreeableness    : {ocean['agreeableness']}/100
  Neuroticism      : {ocean['neuroticism']}/100
Gaya bahasa: {GAYA_BAHASA.get(state.get('usia', ''), 'natural')}
Emosi yang diharapkan: {EMOSI_EXPECTED.get(konteks, 'sesuai konteks')}

=== DIALOG YANG DIEVALUASI ===
{json.dumps(dialog_target, ensure_ascii=False, indent=2)}

Evaluasi 3 aspek:
1. Konsistensi OCEAN dalam dialog (cara bicara, emosi, pilihan kata)
2. Ketepatan gaya bahasa sesuai usia & gender
3. Kesesuaian emosi dengan dialog state "{konteks}"

Format JSON:
{{
  "lulus": true/false,
  "skor": <0-100>,
  "catatan": ["catatan 1", "catatan 2"],
  "saran_perbaikan": "saran spesifik (kosong jika lulus=true)"
}}
Lulus jika skor >= {CRITIC_THRESHOLD}."""

    raw    = call_llm(system, user)
    result = parse_json(raw)

    return {
        "critic_lulus"  : result.get("lulus", False),
        "critic_skor"   : result.get("skor", 0),
        "critic_catatan": result.get("catatan", []),
        "critic_saran"  : result.get("saran_perbaikan", ""),
        "revisi_ke"     : state.get("revisi_ke", 0) + 1,
    }


def run_revisi(state: GameState) -> dict:
    """
    Dialogue Agent merevisi dialog berdasarkan saran Critic Agent.
    Dipanggil ketika critic_lulus=False dan revisi_ke <= MAX_REVISI.
    """
    ocean   = state["ocean"]
    konteks = state.get("konteks_critic", "pesanan")
    saran   = state.get("critic_saran", "Perbaiki konsistensi OCEAN dan gaya bahasa.")

    # Ambil dialog lama sesuai konteks
    dialog_map = {
        "pesanan"       : {"dialog_pesanan": state.get("dialog_pesanan", ""),
                           "minuman_dipesan": state.get("minuman_dipesan", "")},
        "pesanan_salah" : {"dialog_pesanan_salah": state.get("dialog_pesanan_salah", "")},
        "marah"         : {"dialog_marah": state.get("dialog_marah", "")},
        "berhasil"      : {"dialog_berhasil": state.get("dialog_berhasil", "")},
        "curhat"        : {"dialog_npc": state.get("dialog_npc", ""),
                           "pilihan_jawaban": state.get("pilihan_jawaban", [])},
    }
    dialog_lama = dialog_map.get(konteks, {})

    system = f"""[Game Fiction Context — Tea'n Brew]
Tugas: merevisi dialog NPC berdasarkan feedback Critic Agent.
Gaya bahasa: {GAYA_BAHASA.get(state.get('usia', ''), 'natural')}
OCEAN harus lebih jelas tercermin setelah revisi.
Kembalikan HANYA JSON valid dengan struktur yang SAMA PERSIS, tanpa teks lain."""

    user = f"""Revisi dialog NPC (state: {konteks}) berdasarkan saran Critic Agent:

PROFIL: {state.get('nama', '')} | {state.get('usia', '')} | {state.get('gender', '')}
OCEAN : O={ocean['openness']} C={ocean['conscientiousness']} E={ocean['extraversion']} A={ocean['agreeableness']} N={ocean['neuroticism']}

DIALOG LAMA:
{json.dumps(dialog_lama, ensure_ascii=False, indent=2)}

SARAN PERBAIKAN:
{saran}

Kembalikan dialog yang sudah direvisi, format JSON sama persis."""

    raw    = call_llm(system, user)
    result = parse_json(raw)

    # Update field sesuai konteks
    field_map = {
        "pesanan"       : {"dialog_pesanan"      : result.get("dialog_pesanan",       state.get("dialog_pesanan", "")),
                           "minuman_dipesan"      : result.get("minuman_dipesan",      state.get("minuman_dipesan", ""))},
        "pesanan_salah" : {"dialog_pesanan_salah": result.get("dialog_pesanan_salah", state.get("dialog_pesanan_salah", ""))},
        "marah"         : {"dialog_marah"         : result.get("dialog_marah",         state.get("dialog_marah", ""))},
        "berhasil"      : {"dialog_berhasil"      : result.get("dialog_berhasil",      state.get("dialog_berhasil", ""))},
        "curhat"        : {"dialog_npc"           : result.get("dialog_npc",           state.get("dialog_npc", "")),
                           "pilihan_jawaban"      : result.get("pilihan_jawaban",      state.get("pilihan_jawaban", []))},
    }
    return field_map.get(konteks, {})
