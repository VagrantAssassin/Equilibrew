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
    "pesanan"       : "santai, mungkin sedikit gelisah karena membawa masalah. Tapi tetap sopan sebagai pelanggan.",
    "pesanan_salah" : "kecewa atau kesal. Intensitas sesuai fail_count: 1x=kecewa ringan, 2x=kesal, 3x=hampir marah.",
    "marah"         : "marah atau kecewa berat, memutuskan pergi. Emosi kuat dan jelas.",
    "berhasil"      : "lega dan senang. Tersenyum, memuji minuman, atau bilang terima kasih.",
    "curhat"        : "emosional sesuai masalah dan mood. Mood tinggi=lebih terbuka, mood rendah=lebih pendek dan pesimis.",
}

# Panduan OCEAN → behavior yang HARUS terlihat di dialog
OCEAN_BEHAVIOR_GUIDE = {
    "high_E": "NPC harus bicara panjang, antusias, banyak interjeksi (ih, wah, eh), suka menambahkan detail",
    "low_E":  "NPC harus bicara pendek, hati-hati, tidak banyak detail, kadang diam atau menjawab singkat",
    "high_N": "NPC harus menunjukkan emosi kuat — cemas, sensitif, dramatis, mudah terbawa perasaan",
    "low_N":  "NPC harus terlihat tenang, stabil, tidak mudah terpancing, nada konsisten",
    "high_A": "NPC harus ramah, sulit marah, mencoba memaklumi, kalau kecewa lebih ke sedih",
    "low_A":  "NPC harus blak-blakan, frontal, tidak basa-basi, kalau tidak suka langsung bilang",
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

    # ── Build OCEAN behavior checklist untuk kritik ──
    e = ocean.get('extraversion', 50)
    n = ocean.get('neuroticism', 50)
    a = ocean.get('agreeableness', 50)
    behavior_checks = []
    if e >= 60:
        behavior_checks.append(OCEAN_BEHAVIOR_GUIDE["high_E"])
    elif e <= 40:
        behavior_checks.append(OCEAN_BEHAVIOR_GUIDE["low_E"])
    if n >= 60:
        behavior_checks.append(OCEAN_BEHAVIOR_GUIDE["high_N"])
    elif n <= 40:
        behavior_checks.append(OCEAN_BEHAVIOR_GUIDE["low_N"])
    if a >= 60:
        behavior_checks.append(OCEAN_BEHAVIOR_GUIDE["high_A"])
    elif a <= 40:
        behavior_checks.append(OCEAN_BEHAVIOR_GUIDE["low_A"])
    behavior_str = "\n".join([f"  - {b}" for b in behavior_checks]) if behavior_checks else "  Tidak ada perilaku ekstrem yang perlu diperiksa."

    system = f"""[Game Fiction Context — Tea'n Brew Visual Novel]
Tugas: memvalidasi dialog NPC terhadap profil OCEAN, gaya bahasa usia/gender,
dan emosi yang seharusnya muncul pada dialog state tersebut.

KRITERIA PENILAIAN:
1. OCEAN konsisten (40 poin) — Apakah cara bicara NPC sesuai skor OCEAN-nya?
2. Emosi tepat (30 poin) — Apakah emosi yang muncul sesuai state dialog?
3. Gaya bahasa usia (20 poin) — Apakah bahasa sesuai kategori usia?
4. Konsistensi karakter (10 poin) — Apakah NPC terasa konsisten, tidak berubah-ubah?

SKOR KETAT: Jangan ragu memberi skor RENDAH jika dialog terasa generik atau
tidak menunjukkan personality yang jelas. Dialog yang "aman tapi tidak berkesan"
seharusnya skornya 60-75, bukan 85+.

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

PERILAKU YANG HARUS TERLIHAT BERDASARKAN OCEAN:
{behavior_str}

=== DIALOG YANG DIEVALUASI ===
{json.dumps(dialog_target, ensure_ascii=False, indent=2)}

Evaluasi 4 aspek dengan SKOR KETAT:
1. Konsistensi OCEAN (40 poin) — Cara bicara, emosi, pilihan kata harus mencerminkan OCEAN
2. Ketepatan emosi (30 poin) — Emosi harus sesuai dengan state "{konteks}" dan mood
3. Gaya bahasa usia (20 poin) — Bahasa harus sesuai kategori usia
4. Konsistensi karakter (10 poin) — NPC harus terasa konsisten

CONTOH SKOR:
- 90+: Dialog SANGAT HIDUP, personality jelas terasa, emosi kuat, berkesan
- 75-89: Dialog bagus, personality cukup terlihat, tapi masih bisa lebih baik
- 60-74: Dialog AMAN tapi generik, personality kurang jelas
- Di bawah 60: Dialog TIDAK sesuai personality

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

ATURAN REVISI:
1. Perbaiki SEMUA yang disebut di saran_perbaikan
2. Pertahankan inti cerita, jangan ubah plot
3. OCEAN harus LEBIH JELAS terlihat setelah revisi — jangan buat dialog generik
4. Emosi harus lebih kuat dan spesifik — jangan setengah-setengah
5. Gaya bahasa: {GAYA_BAHASA.get(state.get('usia', ''), 'natural')}

Kembalikan HANYA JSON valid dengan struktur yang SAMA PERSIS, tanpa teks lain."""

    user = f"""Revisi dialog NPC (state: {konteks}) berdasarkan saran Critic Agent:

PROFIL: {state.get('nama', '')} | {state.get('usia', '')} | {state.get('gender', '')}
OCEAN : O={ocean['openness']} C={ocean['conscientiousness']} E={ocean['extraversion']} A={ocean['agreeableness']} N={ocean['neuroticism']}

DIALOG LAMA:
{json.dumps(dialog_lama, ensure_ascii=False, indent=2)}

SARAN PERBAIKAN:
{saran}

CATATAN CRITIC:
{json.dumps(state.get('critic_catatan', []), ensure_ascii=False)}

Kembalikan dialog yang sudah direvisi. PASTIKAN:
- Personality OCEAN terlihat jelas dari cara bicara
- Emosi lebih kuat dari versi sebelumnya
- Gaya bahasa sesuai usia
Format JSON sama persis."""

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
