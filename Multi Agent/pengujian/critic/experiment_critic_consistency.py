"""
experiment_critic_consistency.py
--------------------------------
Eksperimen Critic Agent — Consistency-Based Uncertainty (paraphrase invariance).

METODE
------
Mengukur reliabilitas Critic Agent (LLM-as-a-Judge) lewat *invariansi terhadap
parafrase*. Hipotesis: jika judge konsisten, maka menilai teks yang MAKNA-nya
SAMA tetapi KATA-nya diubah harus menghasilkan vektor skor yang HAMPIR identik.

ALUR (per dialog: GOOD dan BAD)
-------------------------------
  1. Evaluasi DIALOG ORIGINAL sekali      -> vektor referensi v0 (8 dimensi FED).
  2. REPHRASE SEKALI SAJA (teks frozen).  -> background, masalah_hari_ini, dan
                                             dialog_pesanan diubah (hasil dari
                                             Tabel 4.2 & 4.3, di-hardcode).
  3. Loop N kali (default 20):
       - input REPHRASE YANG SAMA ke critic agent
       - tiap run menghasilkan vektor vi + skor total + teks justifikasi
       - hitung cosine_similarity(vi, v0)
  4. Agregasi -> uncertainty = 1 - mean(cosine similarity).

Perbedaan dengan experiment_critic_uncertainty.py:
  File LAMA : rephrase dilakukan BERULANG tiap iterasi (N teks berbeda).
  File INI  : rephrase SEKALI lalu diinputkan N kali (consistency based).

Nilai yang dicatat per iterasi (sesuai permintaan):
  - iterasi            : urutan input (1..N)
  - output_teks        : justifikasi critic (Chain-of-Thought, output berbentuk teks)
  - skor_critic        : skor total (rata-rata 8 dimensi FED)
  - cosine_similarity  : cosine(v0, vi) terhadap original

CATATAN NONDETERMINISME
-----------------------
CRITIC_MODEL (GPT-5.6-sol) terdeteksi sebagai *reasoning model* oleh
llm_client.sehingga parameter sampling (temperature) DI-SKIP secara otomatis.
Konsekuensinya, 20 run berulang dengan input identik BISA menghasilkan output
yang sama persis (cosine = 1.0 -> uncertainty = 0). Hal ini tetap interpretable:
"critic agent 100% konsisten (deterministik)". Untuk mendapatkan variasi
sampling, gunakan critic model non-reasoning (mis. llama) atau nilai default.

Cara menjalankan:
  cd "d:\\File Unity\\Equilibrew\\Multi Agent"
  python experiment_critic_consistency.py                    # default: N=20, both seed
  python experiment_critic_consistency.py -n 10 --seed good
  python experiment_critic_consistency.py --seed bad --out-dir hasil

Output:
  exp_critic_consistency/results.json    -> hasil lengkap (reference + iterasi)
  exp_critic_consistency/iterations.csv  -> 1 baris per iterasi (4 field diminta)
"""

import argparse
import csv
import json
import os
import statistics
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from config import CRITIC_THRESHOLD
from agents.critic_agent import run as critic_run


# ─────────────────────────────────────────────────────────────────────────────
# 8 DIMENSI FED (urutan harus sama persis dengan critic_agent.py)
# ─────────────────────────────────────────────────────────────────────────────
FED_DIMS = [
    "interesting", "engaging", "specific", "relevant",
    "correct", "semantically_appropriate", "understandable", "fluent",
]


# ─────────────────────────────────────────────────────────────────────────────
# SEED DATA (dari Tabel 4.2 & 4.3) — original vs rephrase, di-hardcode.
# Bidang faktual (nama, usia, gender, OCEAN, minuman) TIDAK berubah.
# Yang di-rephrase: background, masalah_hari_ini, dialog_pesanan.
# ─────────────────────────────────────────────────────────────────────────────

_PROFILE_BASE = {
    "nama": "Raka",
    "usia": "remaja",
    "gender": "pria",
    "ocean": {
        "openness": 62,
        "conscientiousness": 55,
        "extraversion": 78,
        "agreeableness": 35,
        "neuroticism": 72,
    },
    "minuman_dipesan": "Green Tea",
    "konteks_critic": "pesanan",
}

# Profil (Tabel 4.2) — background & masalah_hari_ini yang di-rephrase (sama utk
# kedua dialog, karena profil Raka dipakai untuk GOOD maupun BAD).
_BACKGROUND_ORIGINAL = (
    "Mahasiswa teknik yang hobi main gitar di kosan. "
    "Tipikal orang yang keliatan cuek tapi sebenernya perhatian."
)
_BACKGROUND_REPHRASE = (
    "Mahasiswa teknik yang senang gitaran di kosan. "
    "Tipe orang yang kelihatannya cuek, padahal aslinya perhatian."
)
_MASALAH_ORIGINAL = (
    "Deadline tugas kelompok dimajukan jadi besok padahal anggota timnya "
    "pada menghilang semua."
)
_MASALAH_REPHRASE = (
    "Batas pengumpulan tugas kelompok dipercepat ke besok, padahal semua "
    "anggota timnya malah menghilang."
)

# Dialog (Tabel 4.3)
_DIALOG_GOOD_ORIGINAL = (
    "Eh kak, akhirnya sampai juga! Pesen Green Tea ya kak, soalnya kepala aku "
    "penuh banget tadi mulai dari deadline tugas kelompok yang dimajuin besok "
    "sampe anggota tim yang pada ngilang. Serius deh, butuh teh buat ngecabin "
    "malam nanti. Makasih ya kak!"
)
_DIALOG_GOOD_REPHRASE = (
    "Eh kak, akhirnya tiba juga! Aku pesan Green Tea ya kak, karena kepala lagi "
    "penuh banget, dari deadline tugas kelompok yang dimajuin jadi besok sampai "
    "anggota tim yang semuanya menghilang. Beneran deh, aku perlu teh buat "
    "ngelewatin malam nanti. Makasih ya kak!"
)
_DIALOG_BAD_ORIGINAL = "Saya ingin memesan green tea, cepat ya saya buru buru."
_DIALOG_BAD_REPHRASE = "Saya mau memesan green tea, cepat ya, saya sedang buru-buru."


def _make_seed(dialog_original: str, dialog_rephrase: str, label: str) -> dict:
    """Bangun pasangan original/rephrase untuk satu label (good/bad)."""
    return {
        "label": label,
        "original": {
            **_PROFILE_BASE,
            "background": _BACKGROUND_ORIGINAL,
            "masalah_hari_ini": _MASALAH_ORIGINAL,
            "dialog_pesanan": dialog_original,
        },
        "rephrase": {
            **_PROFILE_BASE,
            "background": _BACKGROUND_REPHRASE,
            "masalah_hari_ini": _MASALAH_REPHRASE,
            "dialog_pesanan": dialog_rephrase,
        },
    }


SEEDS = {
    "good": _make_seed(_DIALOG_GOOD_ORIGINAL, _DIALOG_GOOD_REPHRASE, "good"),
    "bad":  _make_seed(_DIALOG_BAD_ORIGINAL,  _DIALOG_BAD_REPHRASE,  "bad"),
}


# ─────────────────────────────────────────────────────────────────────────────
# METRIK (pure-python, tanpa dependency eksternal)
# ─────────────────────────────────────────────────────────────────────────────

def cosine_similarity(a, b) -> float:
    """Cosine similarity antara dua vektor numerik. 0.0 jika salah satu nol."""
    na = statistics.fsum(x * x for x in a) ** 0.5
    nb = statistics.fsum(y * y for y in b) ** 0.5
    if na == 0.0 or nb == 0.0:
        return 0.0
    num = statistics.fsum(x * y for x, y in zip(a, b))
    return num / (na * nb)


# ─────────────────────────────────────────────────────────────────────────────
# EVALUASI DAN RUNNER
# ─────────────────────────────────────────────────────────────────────────────

def evaluate(seed: dict) -> dict:
    """Jalankan critic_agent terhadap satu seed, kembalikan skor + vektor + teks."""
    state = {
        "nama": seed["nama"],
        "usia": seed["usia"],
        "gender": seed["gender"],
        "ocean": seed["ocean"],
        "minuman_dipesan": seed.get("minuman_dipesan", ""),
        "background": seed.get("background", ""),
        "masalah_hari_ini": seed.get("masalah_hari_ini", ""),
        "dialog_pesanan": seed.get("dialog_pesanan", ""),
        "konteks_critic": seed.get("konteks_critic", "pesanan"),
        "revisi_ke": 0,
        "mood": 50,
    }

    result = critic_run(state)

    dims = result.get("critic_skor_dimensi", {})
    vector = [float(dims.get(d, 0.0)) for d in FED_DIMS]

    return {
        "skor_total": float(result.get("critic_skor", 0.0)),
        "skor_dimensi": dict(dims),
        "vector": vector,
        "justifikasi": result.get("critic_justifikasi", ""),
        "saran": result.get("critic_saran", ""),
        "lulus": result.get("critic_lulus", False),
    }


def run_experiment(seed_pair: dict, n_iters: int) -> dict:
    """Evaluasi original + N kali input rephrase (frozen), hitung cosine tiap run."""
    label = seed_pair["label"]

    print(f"\n{'─' * 72}")
    print(f"  SEED: {label.upper()}  |  N input rephrase: {n_iters}  |  threshold: {CRITIC_THRESHOLD}")
    print(f"{'─' * 72}")

    # 1) Referensi: dialog original
    print("  [referensi] Evaluasi dialog ORIGINAL...")
    ref = evaluate(seed_pair["original"])
    ref_vec = ref["vector"]
    print(f"              skor = {ref['skor_total']:.3f}  |  lulus = {ref['lulus']}")
    print(f"              FED  = {[round(v, 1) for v in ref_vec]}")

    # 2) Loop N kali: input rephrase yang SAMA (frozen)
    records = []
    cosines = []
    diffs = []

    for i in range(1, n_iters + 1):
        ev = evaluate(seed_pair["rephrase"])
        cos = cosine_similarity(ref_vec, ev["vector"])
        diff = abs(ev["skor_total"] - ref["skor_total"])

        cosines.append(cos)
        diffs.append(diff)

        records.append({
            "iterasi": i,
            "teks": seed_pair["rephrase"]["dialog_pesanan"],
            "output_teks": ev["justifikasi"],
            "skor_critic": round(ev["skor_total"], 4),
            "skor_dimensi": ev["skor_dimensi"],
            "lulus": ev["lulus"],
            "cosine_similarity_vs_original": round(cos, 6),
            "abs_diff_skor": round(diff, 4),
        })

        print(f"  [{i:>2}] skor = {ev['skor_total']:.3f}  |  cosine = {cos:.4f}  |  |Δ| = {diff:.3f}")

    # 3) Agregasi
    mean_cos = statistics.fmean(cosines) if cosines else 0.0
    std_cos = statistics.pstdev(cosines) if len(cosines) > 1 else 0.0
    mean_diff = statistics.fmean(diffs) if diffs else 0.0

    summary = {
        "n_iterations": n_iters,
        "mean_cosine_similarity": round(mean_cos, 4),
        "std_cosine_similarity": round(std_cos, 4),
        "uncertainty": round(1.0 - mean_cos, 4),
        "mean_abs_score_diff": round(mean_diff, 4),
    }

    print(f"\n  RINGKASAN {label.upper()}  =>  mean cosine = {mean_cos:.4f}  "
          f"|  uncertainty = {1.0 - mean_cos:.4f}")

    return {
        "label": label,
        "threshold": CRITIC_THRESHOLD,
        "original": {
            "teks": seed_pair["original"]["dialog_pesanan"],
            "skor_total": round(ref["skor_total"], 4),
            "skor_dimensi": ref["skor_dimensi"],
            "justifikasi": ref["justifikasi"],
            "lulus": ref["lulus"],
        },
        "summary": summary,
        "iterations": records,
    }


# ─────────────────────────────────────────────────────────────────────────────
# MAIN
# ─────────────────────────────────────────────────────────────────────────────

def main() -> int:
    parser = argparse.ArgumentParser(
        description="Critic Agent — Consistency-Based Uncertainty (paraphrase invariance)"
    )
    parser.add_argument("-n", "--iterations", type=int, default=20,
                        help="Jumlah input rephrase yang SAMA ke critic (default 20)")
    parser.add_argument("--seed", choices=["good", "bad", "both"], default="both",
                        help="Seed yang diuji (default both)")
    parser.add_argument("--out-dir", default="exp_critic_consistency",
                        help="Folder output (default exp_critic_consistency)")
    args = parser.parse_args()

    os.makedirs(args.out_dir, exist_ok=True)

    chosen = ["good", "bad"] if args.seed == "both" else [args.seed]

    print()
    print("╔" + "═" * 70 + "╗")
    print("║" + "  CRITIC AGENT — CONSISTENCY-BASED UNCERTAINTY".center(70) + "║")
    print("║" + f"  N input rephrase per dialog: {args.iterations}".center(70) + "║")
    print("╚" + "═" * 70 + "╝")

    all_results = []
    for name in chosen:
        all_results.append(run_experiment(SEEDS[name], args.iterations))

    # ── Simpan JSON ──────────────────────────────────────────────────────────
    json_path = os.path.join(args.out_dir, "results.json")
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(all_results, f, indent=2, ensure_ascii=False)
    print(f"\n✓ Saved JSON: {json_path}")

    # ── Simpan CSV perbandingan (original + tiap iterasi rephrase) ──────────
    # Tabel berisi: label, teks (dialog yang dinilai), skor critic.
    csv_path = os.path.join(args.out_dir, "iterations.csv")
    with open(csv_path, "w", encoding="utf-8", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["label", "teks", "skor_critic"])
        for res in all_results:
            # Baris referensi (dialog dasar / original)
            writer.writerow([
                res["label"],
                res["original"]["teks"],
                res["original"]["skor_total"],
            ])
            # Baris tiap iterasi (input rephrase)
            for rec in res["iterations"]:
                writer.writerow([
                    res["label"],
                    rec["teks"],
                    rec["skor_critic"],
                ])
    print(f"✓ Saved CSV (tabel: label|teks|skor_critic): {csv_path}")

    # ── Simpan CSV detail per iterasi (field lengkap untuk analisis) ─────────
    detail_path = os.path.join(args.out_dir, "iterations_detail.csv")
    with open(detail_path, "w", encoding="utf-8", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["label", "iterasi", "teks", "output_teks",
                         "skor_critic", "cosine_similarity"])
        for res in all_results:
            for rec in res["iterations"]:
                writer.writerow([
                    res["label"],
                    rec["iterasi"],
                    rec["teks"],
                    rec["output_teks"],
                    rec["skor_critic"],
                    rec["cosine_similarity_vs_original"],
                ])
    print(f"✓ Saved CSV (detail iterasi): {detail_path}\n")

    # ── Ringkasan akhir di layar ─────────────────────────────────────────────
    print("=" * 72)
    print("  RINGKASAN AKHIR")
    for res in all_results:
        s = res["summary"]
        print(f"  {res['label']:<6} mean_cosine={s['mean_cosine_similarity']}  "
              f"uncertainty={s['uncertainty']}  mean|Δ|={s['mean_abs_score_diff']}")
    print("=" * 72)

    return 0


if __name__ == "__main__":
    sys.exit(main())