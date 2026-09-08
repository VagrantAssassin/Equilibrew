"""
experiment_critic_uncertainty.py
---------------------------------
Eksperimen Critic Agent — Uncertainty Consistency-Based (SelfCheckGPT-style).

Metode
------
Mengukur reliabilitas Critic Agent (LLM-as-a-Judge) melalui *invariansi terhadap
parafrase*. Jika judge konsisten, maka menilai ulang teks yang maknanya SAMA
(tetapi di-rephrase kata-katanya) harus menghasilkan skor yang HAMPIR IDENTIK.
Semakin besar selisih skor antar-parafrase -> semakin tinggi ketidakpastian
(uncertainty) judge.

Alur
----
  1. Muat satu SEED (profil + dialog) yang statis/frozen.
  2. Evaluasi referensi (s_0)              -> skor total + 8 dimensi FED.
  3. Loop N iterasi:
       - rephrase text profile (background, masalah) + dialog (makna tetap)
       - evaluasi ulang (s_i)              -> skor total + 8 dimensi FED
       - hitung similarity(s_0, s_i) & similarity antar-rephrase
  4. Agregasi similarity -> skor UNCERTAINTY (1 - mean similarity).

Metrik similarity yang dihitung (semua ditampilkan bersamaan):
  - Cosine similarity     : vektor 8 dimensi FED (terhadap referensi & pairwise)
  - Absolute score diff   : |s_i - s_0| pada skor total
  - Spearman rank corr.   : korelasi urutan 8 dimensi
  - Kendall tau           : kesepakatan urutan 8 dimensi

Catatan penting
---------------
Untuk konteks "pesanan", `critic_agent.run()` langsung membaca:
`dialog_pesanan`, `minuman_dipesan`, `ocean`, `nama/usia/gender`, dan gaya bahasa.
Bidang `background` & `masalah_hari_ini` ikut di-rephrase dan dicatat, tetapi
untuk konteks ini yang paling menggerakkan skor judge adalah TEKS DIALOG.

Cara menjalankan (pastikan .env sudah terisi):
  cd "d:\\File Unity\\Equilibrew\\Multi Agent"
  python experiment_critic_uncertainty.py                 # default: 5 iterasi, 2 seed
  python experiment_critic_uncertainty.py -n 10 --seed good
  python experiment_critic_uncertainty.py -n 3 --seed bad --temperature 0.9
"""

import argparse
import json
import os
import statistics
import sys

# Pastikan folder Multi Agent ada di path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from config import (
    CRITIC_MODEL, DIALOGUE_MODEL, PROFILE_MODEL,
    CRITIC_THRESHOLD,
)
from llm_client import call_llm, parse_json
from agents.critic_agent import run as critic_run


# ─────────────────────────────────────────────────────────────────────────────
# SEED STATIS (frozen profile + dialog)
# ─────────────────────────────────────────────────────────────────────────────

# Urutan 8 dimensi FED (harus sama persis dengan critic_agent.py)
FED_DIMS = [
    "interesting", "engaging", "specific", "relevant",
    "correct", "semantically_appropriate", "understandable", "fluent",
]

GOOD_SEED = {
    "label": "good",
    "konteks_critic": "pesanan",
    "nama": "Raka",
    "usia": "remaja",
    "gender": "pria",
    "background": (
        "Mahasiswa teknik yang hobi main gitar di kosan. "
        "Tipikal orang yang keliatan cuek tapi sebenernya perhatian."
    ),
    "masalah_hari_ini": (
        "Deadline tugas kelompok dimajukan jadi besok padahal anggota timnya "
        "pada menghilang semua."
    ),
    "minuman_dipesan": "Green Tea",
    "ocean": {
        "openness": 62, "conscientiousness": 55, "extraversion": 78,
        "agreeableness": 35, "neuroticism": 72,
    },
    "dialog_pesanan": (
        "Eh kak, akhirnya sampai juga! Pesen Green Tea ya kak, soalnya kepala aku "
        "penuh banget tadi mulai dari deadline tugas kelompok yang dimajuin besok "
        "sampe anggota tim yang pada ngilang. Serius deh, butuh teh buat ngecabin "
        "malam nanti. Makasih ya kak!"
    ),
}

BAD_SEED = {
    "label": "bad",
    "konteks_critic": "pesanan",
    "nama": "Raka",
    "usia": "remaja",
    "gender": "pria",
    "background": (
        "Mahasiswa teknik yang hobi main gitar di kosan. "
        "Tipikal orang yang keliatan cuek tapi sebenernya perhatian."
    ),
    "masalah_hari_ini": (
        "Deadline tugas kelompok dimajukan jadi besok padahal anggota timnya "
        "pada menghilang semua."
    ),
    "minuman_dipesan": "Green Tea",
    "ocean": {
        "openness": 62, "conscientiousness": 55, "extraversion": 78,
        "agreeableness": 35, "neuroticism": 72,
    },
    # Dialog ngaco: formal, pendek, tak emosional — bertentangan dengan OCEAN
    # (E=78 antusias, N=72 emosional, A=35 blak-blakan) & state "pesanan".
    "dialog_pesanan": "Saya ingin memesan green tea, cepat ya saya buru buru.",
}

SEEDS = {"good": GOOD_SEED, "bad": BAD_SEED}

# Bidang teks yang di-rephrase (makna dipertahankan, kata diubah)
REPHRASE_TEXT_FIELDS = ["background", "masalah_hari_ini", "dialog_pesanan"]


# ─────────────────────────────────────────────────────────────────────────────
# METRIK SIMILARITY (pure-python, tanpa dependency eksternal)
# ─────────────────────────────────────────────────────────────────────────────

def _rank(vals):
    """Rank 1-based dengan rata-rata untuk tie."""
    order = sorted(range(len(vals)), key=lambda i: vals[i])
    ranks = [0.0] * len(vals)
    i = 0
    while i < len(vals):
        j = i
        while j < len(vals) and vals[order[j]] == vals[order[i]]:
            j += 1
        avg = (i + j - 1) / 2.0 + 1.0
        for k in range(i, j):
            ranks[order[k]] = avg
        i = j
    return ranks


def _pearson(x, y):
    n = len(x)
    if n == 0:
        return 0.0
    mx = sum(x) / n
    my = sum(y) / n
    num = sum((a - mx) * (b - my) for a, b in zip(x, y))
    dx = statistics.fsum((a - mx) ** 2 for a in x) ** 0.5
    dy = statistics.fsum((b - my) ** 2 for b in y) ** 0.5
    if dx == 0 or dy == 0:
        return 0.0
    return num / (dx * dy)


def cosine_similarity(a, b):
    num = sum(x * y for x, y in zip(a, b))
    na = statistics.fsum(x * x for x in a) ** 0.5
    nb = statistics.fsum(y * y for y in b) ** 0.5
    if na == 0 or nb == 0:
        return 0.0
    return num / (na * nb)


def spearman_rho(a, b):
    return _pearson(_rank(a), _rank(b))


def kendall_tau(a, b):
    n = len(a)
    concordant = discordant = 0
    for i in range(n):
        for j in range(i + 1, n):
            da = a[i] - a[j]
            db = b[i] - b[j]
            if da == 0 or db == 0:
                continue  # abaikan tie (tau-a)
            if (da > 0) == (db > 0):
                concordant += 1
            else:
                discordant += 1
    total = concordant + discordant
    if total == 0:
        return 0.0
    return (concordant - discordant) / total


# ─────────────────────────────────────────────────────────────────────────────
# REPHRASE & EVALUASI
# ─────────────────────────────────────────────────────────────────────────────

def rephrase_text(original: str, kind: str, model: str, temperature: float) -> str:
    """
    Minta LLM mem-parafrase `original` dengan makna TETAP, kata diubah.
    Mengembalikan teks hasil rephrase.
    """
    if not original:
        return original

    system = (
        "[Game Fiction Context — Tea'n Brew]\n"
        "Anda adalah alat parafrase teks fiksi untuk penelitian evaluasi dialog.\n"
        "Tugas: tulis ulang teks agar BAHASANYA berbeda, tetapi MAKNA dan FAKTA-nya "
        "harus 100% tetap sama. Jangan menambah informasi baru, jangan menghapus "
        "informasi, jangan mengubah nama, minuman, angka, atau detail faktual apa pun.\n"
        "Kembalikan HANYA JSON valid, tanpa teks lain."
    )

    user = (
        f"Parafrase {kind} berikut sehingga maknanya identik tetapi pilihan katanya "
        f"berubah secara natural. Pertahankan gaya bahasa aslinya (mis. gaul untuk "
        f"remaja), panjang yang setara, dan semua detail faktual.\n\n"
        f"Teks asli:\n\"{original}\"\n\n"
        f"Format JSON:\n{{\"teks\": \"hasil parafrase\"}}"
    )

    raw = call_llm(
        system, user, model=model,
        temperature=temperature,
    )
    result = parse_json(raw)
    return result.get("teks", original)


def rephrase_seed(seed: dict, model: str, temperature: float) -> dict:
    """
    Salin seed, lalu rephrase semua bidang teks. Bidang faktual (nama, usia,
    gender, ocean, minuman) TETAP — hanya teks yang diubah.
    """
    new_seed = dict(seed)
    for field in REPHRASE_TEXT_FIELDS:
        new_seed[field] = rephrase_text(seed.get(field, ""), field, model, temperature)
    return new_seed


def evaluate(seed: dict) -> dict:
    """
    Jalankan critic_agent terhadap seed, kembalikan skor total + vektor 8 dimensi.
    """
    state = {
        "nama": seed["nama"],
        "usia": seed["usia"],
        "gender": seed["gender"],
        "ocean": seed["ocean"],
        "konteks_critic": seed["konteks_critic"],
        "revisi_ke": 0,
        "mood": 50,
        "dialog_pesanan": seed.get("dialog_pesanan", ""),
        "minuman_dipesan": seed.get("minuman_dipesan", ""),
        "background": seed.get("background", ""),
        "masalah_hari_ini": seed.get("masalah_hari_ini", ""),
    }
    result = critic_run(state)
    dims = result.get("critic_skor_dimensi", {})
    # Pastikan urutan vektor konsisten & semua dimensi ada
    vector = [float(dims.get(d, 0.0)) for d in FED_DIMS]
    total = result.get("critic_skor", 0.0)
    if not dims:
        total = float(total)
    return {
        "skor_total": float(total),
        "skor_dimensi": dict(dims),
        "vector": vector,
        "lulus": result.get("critic_lulus", False),
    }


def _sim_between(ref_vec, ref_total, vecs, totals, fn_sim):
    """Daftar similarity antara tiap item vs referensi (semua metrik vektor)."""
    return [fn_sim(ref_vec, v) for v in vecs]


def _pairwise_sim(vecs, fn_sim):
    """Semua pasangan similarity di antara rephrase (tanpa referensi)."""
    out = []
    for i in range(len(vecs)):
        for j in range(i + 1, len(vecs)):
            out.append(fn_sim(vecs[i], vecs[j]))
    return out


# ─────────────────────────────────────────────────────────────────────────────
# RUNNER
# ─────────────────────────────────────────────────────────────────────────────

def _print_separator(title):
    print("\n" + "=" * 72)
    print(f"  {title}")
    print("=" * 72)


def run_experiment(seed: dict, n_iters: int, model: str, temperature: float) -> dict:
    label = seed["label"]
    print(f"\n{'─' * 72}")
    print(f"  SEED: {label.upper()}  |  N iterasi rephrase: {n_iters}  |  threshold: {CRITIC_THRESHOLD}")
    print(f"{'─' * 72}")

    # 1) Evaluasi referensi
    print("  [0] Evaluasi referensi (s_0)...")
    ref = evaluate(seed)
    print(f"      skor_total = {ref['skor_total']:.3f} / 5.0  |  lulus = {ref['lulus']}")
    print(f"      vektor FED = {[round(v, 1) for v in ref['vector']]}")

    # 2) Loop rephrase + evaluasi
    vecs, totals, absdiffs = [], [], []
    records = []
    for i in range(1, n_iters + 1):
        print(f"  [{i}] Re-phrase teks profile + dialog...", end="", flush=True)
        try:
            rephrased = rephrase_seed(seed, model, temperature)
        except Exception as e:
            print(f" ✗ rephrase error: {e}")
            continue
        ev = evaluate(rephrased)
        diff = abs(ev["skor_total"] - ref["skor_total"])
        vecs.append(ev["vector"])
        totals.append(ev["skor_total"])
        absdiffs.append(diff)
        print(f" skor={ev['skor_total']:.3f}  |Δ|={diff:.3f}")

        records.append({
            "iterasi": i,
            "skor_total": ev["skor_total"],
            "abs_diff": diff,
            "skor_dimensi": ev["skor_dimensi"],
            "dialog_rephrased": rephrased.get("dialog_pesanan", ""),
            "background_rephrased": rephrased.get("background", ""),
            "masalah_rephrased": rephrased.get("masalah_hari_ini", ""),
        })

    if not vecs:
        print("  [ERROR] Tidak ada iterasi berhasil.")
        return {"seed": label, "reference": ref, "iterations": [], "error": "no successful iterations"}

    # 3) Hitung similarity (vs referensi & pairwise)
    cos_vs = _sim_between(ref["vector"], ref["skor_total"], vecs, totals, cosine_similarity)
    rho_vs = _sim_between(ref["vector"], ref["skor_total"], vecs, totals, spearman_rho)
    tau_vs = _sim_between(ref["vector"], ref["skor_total"], vecs, totals, kendall_tau)

    cos_pw = _pairwise_sim(vecs, cosine_similarity)
    rho_pw = _pairwise_sim(vecs, spearman_rho)
    tau_pw = _pairwise_sim(vecs, kendall_tau)

    # 4) Agregasi
    def _aggregate(name, vs_ref, pairwise, upper_tau=False):
        mu = statistics.fmean(vs_ref) if vs_ref else 0.0
        sd = statistics.pstdev(vs_ref) if len(vs_ref) > 1 else 0.0
        # uncertainty = 1 - mean similarity (dissimilarity)
        unc = 1.0 - mu
        return {
            "metrik": name,
            "mean_sim_vs_ref": round(mu, 4),
            "std_sim_vs_ref": round(sd, 4),
            "mean_sim_pairwise": round(statistics.fmean(pairwise), 4) if pairwise else None,
            "uncertainty": round(unc, 4),
        }

    metrics = [
        _aggregate("cosine_similarity (FED vector)", cos_vs, cos_pw),
        _aggregate("spearman_rho (FED vector)", rho_vs, rho_pw),
        _aggregate("kendall_tau (FED vector)", tau_vs, tau_pw),
    ]

    mean_abs_diff = statistics.fmean(absdiffs) if absdiffs else 0.0
    metrics.append({
        "metrik": "absolute_score_diff (total)",
        "mean_abs_diff": round(mean_abs_diff, 4),
        "std_abs_diff": round(statistics.pstdev(absdiffs), 4) if len(absdiffs) > 1 else 0.0,
        # normalisasi ke [0,1]: skala skor 1-5 → selisih maksimum 4
        "uncertainty": round(mean_abs_diff / 4.0, 4),
    })

    result = {
        "seed": label,
        "threshold": CRITIC_THRESHOLD,
        "n_iterations_requested": n_iters,
        "n_iterations_success": len(records),
        "reference": {
            "skor_total": ref["skor_total"],
            "skor_dimensi": ref["skor_dimensi"],
            "lulus": ref["lulus"],
        },
        "skor_total_series": [ref["skor_total"]] + totals,
        "metrics": metrics,
        "iterations": records,
    }

    # ── Print ringkasan ──
    print()
    for m in metrics:
        if m["metrik"].startswith("absolute"):
            print(f"      • {m['metrik']:32s}  mean|Δ|={m['mean_abs_diff']:.3f}  unc={m['uncertainty']:.3f}")
        else:
            print(f"      • {m['metrik']:32s}  mean_sim={m['mean_sim_vs_ref']:.3f}  unc={m['uncertainty']:.3f}")

    return result


# ─────────────────────────────────────────────────────────────────────────────
# MAIN
# ─────────────────────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(description="Critic Agent Uncertainty Consistency-Based Test")
    parser.add_argument("-n", "--iterations", type=int, default=5,
                        help="Jumlah iterasi rephrase (default 5)")
    parser.add_argument("--seed", choices=["good", "bad", "both"], default="both",
                        help="Seed yang diuji (default both)")
    parser.add_argument("--model", default=DIALOGUE_MODEL,
                        help="Model untuk rephrase (default DIALOGUE_MODEL)")
    parser.add_argument("--temperature", type=float, default=0.7,
                        help="Temperature untuk rephrase (default 0.7)")
    parser.add_argument("--out-dir", default="exp_critic_uncertainty",
                        help="Folder output (default exp_critic_uncertainty)")
    args = parser.parse_args()

    os.makedirs(args.out_dir, exist_ok=True)

    chosen = ["good", "bad"] if args.seed == "both" else [args.seed]

    print()
    print("╔" + "═" * 70 + "╗")
    print("║" + "  CRITIC AGENT — UNCERTAINTY CONSISTENCY-BASED TEST".center(70) + "║")
    print("║" + f"  Re-phrase model: {args.model}".center(70) + "║")
    print("╚" + "═" * 70 + "╝")

    all_results = []
    for seed_name in chosen:
        seed = SEEDS[seed_name]
        res = run_experiment(seed, args.iterations, args.model, args.temperature)
        all_results.append(res)

    # ── Simpan JSON ──
    json_path = os.path.join(args.out_dir, "uncertainty_results.json")
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(all_results, f, indent=2, ensure_ascii=False)
    print(f"\n✓ Saved JSON: {json_path}")

    # ── Simpan CSV ringkasan metrik ──
    csv_path = os.path.join(args.out_dir, "uncertainty_summary.csv")
    with open(csv_path, "w", encoding="utf-8") as f:
        f.write("seed,metrik,mean_sim,uncertainty\n")
        for res in all_results:
            for m in res.get("metrics", []):
                name = m["metrik"]
                if name.startswith("absolute"):
                    mean_sim = ""
                    unc = m["uncertainty"]
                else:
                    mean_sim = m["mean_sim_vs_ref"]
                    unc = m["uncertainty"]
                f.write(f'{res["seed"]},{name},{mean_sim},{unc}\n')
    print(f"✓ Saved CSV: {csv_path}\n")

    return 0


if __name__ == "__main__":
    sys.exit(main())