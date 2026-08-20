"""
agents/critic_agent.py
----------------------
Critic Agent — quality gate dalam pola Actor-Critic.

Memvalidasi dialog dari Dialogue Agent terhadap:
  1. Konsistensi kepribadian OCEAN
  2. Kesesuaian gaya bahasa usia & gender
  3. Kesesuaian emosi dengan dialog state aktif

Lulus jika skor >= CRITIC_THRESHOLD (default 70).
"""

import json
import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))

from config     import (
    GAYA_BAHASA, CRITIC_THRESHOLD, CRITIC_MODEL,
    CRITIC_TEMPERATURE, CRITIC_FREQUENCY_PENALTY, CRITIC_PRESENCE_PENALTY,
)
from llm_client import call_llm, parse_json
from state      import GameState


# Panduan emosi yang diharapkan per dialog state
EMOSI_EXPECTED = {
    "pesanan"       : "santai, mungkin sedikit gelisah karena membawa masalah. Tapi tetap sopan sebagai pelanggan.",
    "pesanan_salah" : "kecewa atau kesal. Intensitas sesuai fail_count: 1x=kecewa ringan, 2x=kesal, 3x=hampir marah.",
    "marah"         : "marah atau kecewa berat, memutuskan pergi. Emosi kuat dan jelas.",
    "berhasil"      : "lega dan senang. Tersenyum, memuji minuman, atau bilang terima kasih.",
    "curhat"        : "emosional sesuai masalah dan mood. Mood tinggi=lebih terbuka, mood rendah=lebih pendek dan pesimis.",
    "reaksi"        : "merespons jawaban pemain secara spesifik dengan emosi yang sesuai nada jawaban, tanpa menutup sesi.",
    "closing"       : "menyimpulkan sesi curhat, berterima kasih, dan menutup percakapan secara konsisten dengan mood akhir.",
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
        "reaksi"        : {"reaksi_npc": state.get("reaksi_npc", "")},
        "closing"       : {"dialog_closing": state.get("dialog_closing", "")},
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
Tugas: Evaluasi kualitas dialog NPC menggunakan framework G-Eval dengan dimensi FED
(Fine-grained Evaluation of Dialogue). Anda adalah juri (LLM-as-a-Judge) yang menilai
dialog berdasarkan 8 dimensi turn-level FED (Mehri & Eskenazi, 2020).

EVALUATION CRITERIA (8 Dimensi FED, skala Likert 1-5):
1. interesting           — Apakah dialog menarik untuk dibaca? Tidak membosankan?
2. engaging              — Apakah dialog membuat pembaca ingin tahu lebih lanjut?
3. specific              — Apakah dialog mengandung detail spesifik, bukan generik?
4. relevant              — Apakah dialog relevan dengan konteks state dan karakter?
5. correct               — Apakah dialog sesuai fakta (OCEAN, usia, gender, situasi)?
6. semantically_appropriate — Apakah makna dialog sesuai dengan konteks percakapan?
7. understandable        — Apakah dialog mudah dipahami? Tidak ambigu?
8. fluent                — Apakah dialog lancar secara linguistik? Natural?

SKALA LIKERT 1-5:
- 5 = Sangat Baik (tidak ada kekurangan yang terlihat)
- 4 = Baik (ada kekurangan minor tapi tidak mengganggu)
- 3 = Cukup (ada beberapa kekurangan yang perlu diperbaiki)
- 2 = Kurang (banyak kekurangan, perlu revisi signifikan)
- 1 = Sangat Kurang (tidak memenuhi kriteria sama sekali)

THRESHOLD: Dialog lulus jika RATA-RATA skor >= {CRITIC_THRESHOLD}/5.0.
(Zheng et al., 2023: LLM-as-a-Judge agreement >80% pada threshold 4.0)

Kembalikan HANYA JSON valid, tanpa teks lain."""

    user = f"""Evaluasi dialog NPC (state: {konteks}) dengan G-Eval + FED:

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

EVALUATION STEPS (Chain-of-Thought — lakukan secara berurutan):
Langkah 1: Baca dialog dengan seksama. Identifikasi siapa yang bicara dan konteksnya.
Langkah 2: Cocokkan dengan profil OCEAN. Apakah E tinggi → dialog panjang/antusias?
           Apakah N tinggi → emosi kuat? Apakah A tinggi → ramah/lembut?
Langkah 3: Periksa emosi — apakah sesuai dengan state "{konteks}"?
Langkah 4: Periksa gaya bahasa — apakah sesuai kategori usia {state.get('usia', '')}?
Langkah 5: Nilai setiap dimensi FED (1-5) dengan justifikasi singkat.

SCORING FORM:
{{
  "lulus": true/false,
  "skor": <rata-rata 8 dimensi, float>,
  "skor_dimensi": {{
    "interesting": <1-5>,
    "engaging": <1-5>,
    "specific": <1-5>,
    "relevant": <1-5>,
    "correct": <1-5>,
    "semantically_appropriate": <1-5>,
    "understandable": <1-5>,
    "fluent": <1-5>
  }},
  "justifikasi": "<Chain-of-Thought: 3-5 kalimat penalaran sebelum skor akhir>",
  "catatan": ["catatan 1", "catatan 2"],
  "saran_perbaikan": "saran spesifik dan actionable (kosong jika lulus=true)"
}}

Lulus jika skor >= {CRITIC_THRESHOLD}. JANGAN ragu memberi skor rendah jika dialog generik."""

    raw    = call_llm(
        system, user, model=CRITIC_MODEL,
        temperature=CRITIC_TEMPERATURE,
        frequency_penalty=CRITIC_FREQUENCY_PENALTY,
        presence_penalty=CRITIC_PRESENCE_PENALTY,
    )
    result = parse_json(raw)

    # Hitung ulang skor dari dimensi untuk akurasi
    skor_dimensi = result.get("skor_dimensi", {})
    if skor_dimensi and len(skor_dimensi) == 8:
        skor = sum(skor_dimensi.values()) / 8.0
    else:
        skor = float(result.get("skor", 0))

    return {
        "critic_lulus"        : result.get("lulus", False),
        "critic_skor"         : skor,
        "critic_skor_dimensi" : skor_dimensi,
        "critic_catatan"      : result.get("catatan", []),
        "critic_saran"        : result.get("saran_perbaikan", ""),
        "critic_justifikasi"  : result.get("justifikasi", ""),
        "revisi_ke"           : state.get("revisi_ke", 0) + 1,
    }
