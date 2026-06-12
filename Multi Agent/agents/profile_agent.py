"""
agents/profile_agent.py
-----------------------
Profile Agent — dipanggil saat pelanggan baru datang ke kafe.

Tanggung jawab:
  Menghasilkan profil lengkap NPC secara dinamis berdasarkan kategori
  usia dan gender, mencakup:
    - Nama khas Indonesia sesuai usia/gender
    - Background kehidupan (2 kalimat)
    - Masalah spesifik yang dibawa hari ini
    - Skor kepribadian OCEAN (0–100 per dimensi)
    - max_fails: toleransi kesalahan pesanan (1–3)

  Output langsung menggantikan mekanik randomize_profile di game Unity.
"""

import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))

from config     import GAYA_BAHASA
from llm_client import call_llm, parse_json
from state      import GameState


def build_ocean_desc(ocean: dict) -> str:
    """Terjemahkan skor OCEAN ke deskripsi naratif untuk prompt."""
    return "\n".join([
        f"- Openness {ocean['openness']}/100: " +
            ("Imajinatif, kreatif, suka hal baru" if ocean['openness'] > 60
             else "Konservatif, praktis, tidak suka perubahan"),
        f"- Conscientiousness {ocean['conscientiousness']}/100: " +
            ("Terstruktur, teliti, bertanggung jawab" if ocean['conscientiousness'] > 60
             else "Santai, fleksibel, kadang ceroboh"),
        f"- Extraversion {ocean['extraversion']}/100: " +
            ("Cerewet, terbuka, mudah akrab dengan orang asing" if ocean['extraversion'] > 60
             else "Pendiam, tertutup, perlu waktu untuk terbuka"),
        f"- Agreeableness {ocean['agreeableness']}/100: " +
            ("Empatis, lembut, mudah mengalah" if ocean['agreeableness'] > 60
             else "Blak-blakan, tegas, tidak mudah menerima pendapat orang"),
        f"- Neuroticism {ocean['neuroticism']}/100: " +
            ("Sensitif, mudah cemas, emosional" if ocean['neuroticism'] > 60
             else "Stabil, tenang, tidak mudah terbawa perasaan"),
    ])


def run(state: GameState) -> dict:
    """
    Node LangGraph: profile_agent
    Dipanggil di awal saat pelanggan baru tiba (skenario Customer Came).

    Input : state["usia"], state["gender"]
    Output: nama, background, masalah_hari_ini, ocean, max_fails
    """
    usia   = state["usia"]
    gender = state["gender"]

    system = """[Game Fiction Context — Tea'n Brew Visual Novel]
Tugas: merakit profil satu pelanggan café yang unik, realistis, dan relatable.

Aturan:
- Nama harus nama Indonesia yang umum dan sesuai gender.
- Background: tepat 2 kalimat kehidupan NPC — singkat namun berkarakter.
- Masalah hari ini: spesifik, emosional, sesuai usia — bukan generik seperti "lelah kerja".
- Skor OCEAN (0–100) harus logis dan konsisten satu sama lain.
- max_fails: toleransi kesalahan pesanan, 1–3 (dipengaruhi Agreeableness & Neuroticism).
  Agreeableness tinggi + Neuroticism rendah → max_fails lebih tinggi (lebih sabar).
  Sebaliknya → max_fails lebih rendah (cepat marah).

Kembalikan HANYA JSON valid, tanpa markdown, tanpa teks lain."""

    user = f"""Pelanggan baru memasuki kafe. Buat profilnya:
- Kategori usia : {usia}
- Gender        : {gender}
- Gaya bahasa nanti: {GAYA_BAHASA.get(usia, 'natural')}

Format JSON:
{{
  "nama": "...",
  "background": "kalimat 1. kalimat 2.",
  "masalah_hari_ini": "masalah spesifik yang dibawa hari ini",
  "ocean": {{
    "openness": <0-100>,
    "conscientiousness": <0-100>,
    "extraversion": <0-100>,
    "agreeableness": <0-100>,
    "neuroticism": <0-100>
  }},
  "max_fails": <1, 2, atau 3>
}}"""

    raw    = call_llm(system, user)
    result = parse_json(raw)

    return {
        "nama"            : result["nama"],
        "background"      : result["background"],
        "masalah_hari_ini": result["masalah_hari_ini"],
        "ocean"           : result["ocean"],
        "max_fails"       : result.get("max_fails", 2),
        # Init state
        "fail_count"      : 0,
        "dialog_state"    : "pesanan",
        "mood"            : 50,   # MOOD_AWAL — nanti dari Unity
        "riwayat"         : [],
        "revisi_ke"       : 0,
        "konteks_critic"  : "pesanan",
    }
