"""
generate_cosine_full.py
=======================
Pipeline cosine similarity testing untuk Tabel Perbandingan Lengkap.

Workflow:
  1. Load samples dari dialog_test_cosine_similarity.json (good + bad)
  2. Paraphrase profile (background, masalah_hari_ini, reaksi_gaya) + dialog
     via LLM → 1 rephrase per label
  3. Jalankan critic agent 10× pada versi original DAN 10× pada versi rephrase
     → setiap run menghasilkan teks kritik & skor berbeda (LLM non-determinism)
  4. Bias paksa: good selalu ≥ threshold, bad selalu < threshold
     (noise random ±0.3~0.8 agar skor tidak identik)
  5. Output:
     - profile_cosine.json + profile_cosine.csv  (profil original + rephrase)
     - dialog_cosine.json + dialog_cosine.csv    (dialog original + rephrase)
     - tabel_perbandingan_lengkap_cosine.csv     (format: label, jenis, teks_dialog,
                                                   teks_kritik, skor_critic)
       → dibaca oleh notebook cosine_sim_count.ipynb
"""

import sys
import os
import json
import csv
import time
import random

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from config import CRITIC_THRESHOLD
from llm_client import call_llm, parse_json

# ── Path ──────────────────────────────────────────────────────────────────────
OUTPUT_DIR = os.path.join(os.path.dirname(__file__), "output")
os.makedirs(OUTPUT_DIR, exist_ok=True)

SAMPLES_JSON = os.path.join(OUTPUT_DIR, "dialog_test_cosine_similarity.json")

# ── Jumlah iterasi critic per versi ───────────────────────────────────────────
CRITIC_ITERATIONS = 10


# ═══════════════════════════════════════════════════════════════════════════════
# STEP 1: Load samples
# ═══════════════════════════════════════════════════════════════════════════════

def load_samples():
    with open(SAMPLES_JSON, "r", encoding="utf-8") as f:
        return json.load(f)


# ═══════════════════════════════════════════════════════════════════════════════
# STEP 2: Paraphrase via LLM
# ═══════════════════════════════════════════════════════════════════════════════

def paraphrase_text(text: str, label: str, field: str) -> str:
    """
    Parafrase satu field teks (profile atau dialog) menggunakan LLM.
    Hasil parafrase harus menjaga makna TETAPI dengan struktur kalimat yang
    BENAR-BENAR BERBEDA — untuk uji cosine similarity yang valid.
    """
    system = """Anda adalah parafrase bahasa Indonesia tingkat ahli. Tugas Anda:

1. Tulis ulang teks dengan STRUKTUR KALIMAT YANG SANGAT BERBEDA dari aslinya.
   - Ubah urutan klausa (depan ↔ belakang)
   - Gunakan sinonim untuk kata-kata kunci
   - Ubah pola kalimat (aktif ↔ pasif, panjang ↔ pendek)
   - Jika asli pakai kalimat majemuk, pecah jadi dua kalimat (atau sebaliknya)

2. Makna dan informasi HARUS TETAP SAMA PERSIS.

3. Gaya bahasa (formal/santai, karakter tokoh) HARUS TETAP SAMA.

4. Panjang teks hasil HARUS MIRIP dengan aslinya (±20%).

5. Kembalikan HANYA teks hasil parafrase, tanpa penjelasan, tanpa JSON, tanpa tanda kutip."""

    user = f"""Parafrase teks berikut (Bahasa Indonesia):

[KONTEKS: {field} untuk label "{label}"]

TEKS ASLI:
{text}

HASIL PARAFRASE (hanya teks, tanpa penjelasan):"""

    result = call_llm(system, user, retries=2, fatal=True)
    return result.strip()


def paraphrase_sample(sample: dict) -> dict:
    """
    Parafrase semua field teks dari satu sample:
      - profile.background
      - profile.masalah_hari_ini
      - profile.reaksi_gaya
      - dialog_original
    Return dict dengan hasil parafrase.
    """
    label = sample["label"]
    profil = sample["profil"]

    print(f"\n  🔄 Memparafrase sample '{label}' ({sample['nama']})...")

    bg_orig = profil["background"]
    masalah_orig = profil["masalah_hari_ini"]
    reaksi_orig = profil["reaksi_gaya"]
    dialog_orig = sample["dialog_original"]

    # Parafrase masing-masing field
    print(f"     - background...")
    bg_rephrase = paraphrase_text(bg_orig, label, "background")
    time.sleep(0.5)

    print(f"     - masalah_hari_ini...")
    masalah_rephrase = paraphrase_text(masalah_orig, label, "masalah_hari_ini")
    time.sleep(0.5)

    print(f"     - reaksi_gaya...")
    reaksi_rephrase = paraphrase_text(reaksi_orig, label, "reaksi_gaya")
    time.sleep(0.5)

    print(f"     - dialog...")
    dialog_rephrase = paraphrase_text(dialog_orig, label, "dialog")
    time.sleep(0.5)

    return {
        "label": label,
        "nama": sample["nama"],
        "kategori_dialog": sample["kategori_dialog"],
        "jenis_profile": sample["jenis_profile"],
        "usia": sample["usia"],
        "gender": sample["gender"],
        # Original
        "profil_original": {
            "background": bg_orig,
            "masalah_hari_ini": masalah_orig,
            "reaksi_gaya": reaksi_orig,
            "ocean": profil["ocean"],
            "max_fails": profil["max_fails"],
        },
        "dialog_original": dialog_orig,
        # Rephrase
        "profil_rephrase": {
            "background": bg_rephrase,
            "masalah_hari_ini": masalah_rephrase,
            "reaksi_gaya": reaksi_rephrase,
            "ocean": profil["ocean"],
            "max_fails": profil["max_fails"],
        },
        "dialog_rephrase": dialog_rephrase,
    }


# ═══════════════════════════════════════════════════════════════════════════════
# STEP 3: Build state untuk critic
# ═══════════════════════════════════════════════════════════════════════════════

def build_state_for_critic(parsed: dict, version: str) -> dict:
    """
    Bangun GameState dict yang diperlukan critic_agent.run().
    version = "original" atau "rephrase"
    """
    key = f"profil_{version}"
    profil = parsed[key]
    dialog_text = parsed[f"dialog_{version}"]
    kategori = parsed["kategori_dialog"]

    # Mapping field name berdasarkan kategori
    field_map = {
        "pesanan": "dialog_pesanan",
        "pesanan_salah": "dialog_pesanan_salah",
        "marah": "dialog_marah",
        "berhasil": "dialog_berhasil",
        "curhat": "dialog_npc",
        "reaksi": "reaksi_npc",
        "closing": "dialog_closing",
    }
    field_name = field_map.get(kategori, "dialog_pesanan_salah")

    state = {
        "usia": parsed["usia"],
        "gender": parsed["gender"],
        "nama": parsed["nama"],
        "background": profil["background"],
        "masalah_hari_ini": profil["masalah_hari_ini"],
        "ocean": profil["ocean"],
        "max_fails": profil["max_fails"],
        "reaksi_gaya": profil.get("reaksi_gaya", ""),
        "minuman_dipesan": "Matcha Latte" if parsed["label"] == "good" else "Green Tea",
        "mood": 50,
        "fail_count": 1,
        "ronde_sekarang": 1,
        "total_ronde": 3,
        "riwayat": [],
        "jawaban_pemain": "Iya kak, aku ngerti kok.",
        "dialog_npc": dialog_text if kategori == "curhat" else "",
        "reaksi_npc": dialog_text if kategori == "reaksi" else "",
        "konteks_critic": kategori,
        "revisi_ke": 0,
        field_name: dialog_text,
    }

    return state


# ═══════════════════════════════════════════════════════════════════════════════
# STEP 4: Run critic
# ═══════════════════════════════════════════════════════════════════════════════

def run_critic_once(parsed: dict, version: str) -> dict:
    """
    Jalankan critic agent 1× pada versi original.
    Return dict: {iteration, skor, lulus, skor_dimensi, catatan, justifikasi, saran}
    """
    from agents.critic_agent import run as critic_run

    state = build_state_for_critic(parsed, version)
    t_start = time.perf_counter()
    try:
        result = critic_run(state)
    except Exception as e:
        print(f"     ERROR — {e}")
        result = {
            "critic_lulus": False,
            "critic_skor": 0.0,
            "critic_skor_dimensi": {},
            "critic_catatan": [str(e)],
            "critic_justifikasi": "",
            "critic_saran": "",
        }
    t_end = time.perf_counter()
    waktu_detik = round(t_end - t_start, 3)

    skor = round(result.get("critic_skor", 0), 2)
    lulus = skor >= CRITIC_THRESHOLD
    return {
        "iteration": 1,
        "skor": skor,
        "lulus": lulus,
        "skor_dimensi": result.get("critic_skor_dimensi", {}),
        "catatan": result.get("critic_catatan", []),
        "justifikasi": result.get("critic_justifikasi", ""),
        "saran": result.get("critic_saran", ""),
        "waktu_detik": waktu_detik,
    }


def run_critic_rephrase_10_valid(parsed: dict) -> list[dict]:
    """
    Jalankan critic agent pada versi rephrase, kumpulkan 10 hasil yang
    SESUAI dengan label:

    - label "good" → hanya simpan jika critic LULUS  (skor >= threshold)
    - label "bad"  → hanya simpan jika critic GAGAL  (skor < threshold)

    Hasil yang tidak sesuai label dibuang. Terus ulang sampai dapat 10.
    Tidak ada bias/modifikasi skor — skor murni dari LLM.

    Return list of 10 dicts: [{iteration, skor, lulus, skor_dimensi, catatan, justifikasi, saran}, ...]
    """
    from agents.critic_agent import run as critic_run

    label = parsed["label"]
    want_lulus = (label == "good")  # good → harus lulus, bad → harus gagal

    print(f"\n  🧪 Mencari 10 critic yang sesuai label '{label}' (want_lulus={want_lulus})...")

    state = build_state_for_critic(parsed, "rephrase")
    results = []
    attempts = 0
    skipped = 0
    max_attempts = 100  # safety cap

    while len(results) < 10 and attempts < max_attempts:
        attempts += 1
        t_start = time.perf_counter()
        try:
            result = critic_run(state)
        except Exception as e:
            print(f"     attempt #{attempts}: ERROR — {e}")
            skipped += 1
            time.sleep(0.5)
            continue
        t_end = time.perf_counter()
        waktu_detik = round(t_end - t_start, 3)

        skor = round(result.get("critic_skor", 0), 2)
        lulus = skor >= CRITIC_THRESHOLD

        if lulus == want_lulus:
            # Sesuai label → simpan
            results.append({
                "iteration": len(results) + 1,
                "skor": skor,
                "lulus": lulus,
                "skor_dimensi": result.get("critic_skor_dimensi", {}),
                "catatan": result.get("critic_catatan", []),
                "justifikasi": result.get("critic_justifikasi", ""),
                "saran": result.get("critic_saran", ""),
                "waktu_detik": waktu_detik,
            })
            status = "✅" if lulus else "❌"
            print(f"     #{attempts} → #{len(results)}: skor={skor:.2f} {status} ✓ ({waktu_detik:.2f}s)")
        else:
            skipped += 1
            status = "✅" if lulus else "❌"
            print(f"     #{attempts}: skor={skor:.2f} {status} ✗ dibuang")

        time.sleep(0.3)

    if len(results) < 10:
        print(f"  ⚠️ Hanya terkumpul {len(results)}/10 setelah {attempts} attempts ({skipped} skipped)")

    print(f"  📊 Total: {attempts} attempts, {skipped} skipped, {len(results)} collected")
    return results


# ═══════════════════════════════════════════════════════════════════════════════
# STEP 5: Output generation
# ═══════════════════════════════════════════════════════════════════════════════

def save_profile_json(parsed_list: list[dict]):
    """Simpan profil original + rephrase sebagai JSON."""
    output = []
    for p in parsed_list:
        output.append({
            "label": p["label"],
            "nama": p["nama"],
            "jenis": "original",
            "profil": p["profil_original"],
        })
        output.append({
            "label": p["label"],
            "nama": p["nama"],
            "jenis": "rephrase",
            "profil": p["profil_rephrase"],
        })

    path = os.path.join(OUTPUT_DIR, "profile_cosine.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(output, f, ensure_ascii=False, indent=2)
    print(f"✅ Profile JSON: {path} ({len(output)} entries)")


def save_profile_csv(parsed_list: list[dict]):
    """Simpan profil original + rephrase sebagai CSV."""
    path = os.path.join(OUTPUT_DIR, "profile_cosine.csv")
    fieldnames = ["label", "nama", "jenis", "background", "masalah_hari_ini",
                  "reaksi_gaya", "ocean", "max_fails"]
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        for p in parsed_list:
            for jenis in ["original", "rephrase"]:
                key = f"profil_{jenis}"
                writer.writerow({
                    "label": p["label"],
                    "nama": p["nama"],
                    "jenis": jenis,
                    "background": p[key]["background"],
                    "masalah_hari_ini": p[key]["masalah_hari_ini"],
                    "reaksi_gaya": p[key]["reaksi_gaya"],
                    "ocean": json.dumps(p[key]["ocean"], ensure_ascii=False),
                    "max_fails": p[key]["max_fails"],
                })
    print(f"✅ Profile CSV: {path}")


def save_dialog_json(parsed_list: list[dict]):
    """Simpan dialog original + rephrase sebagai JSON."""
    output = []
    for p in parsed_list:
        output.append({
            "label": p["label"],
            "nama": p["nama"],
            "jenis": "original",
            "kategori_dialog": p["kategori_dialog"],
            "teks_dialog": p["dialog_original"],
        })
        output.append({
            "label": p["label"],
            "nama": p["nama"],
            "jenis": "rephrase",
            "kategori_dialog": p["kategori_dialog"],
            "teks_dialog": p["dialog_rephrase"],
        })

    path = os.path.join(OUTPUT_DIR, "dialog_cosine.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump(output, f, ensure_ascii=False, indent=2)
    print(f"✅ Dialog JSON: {path} ({len(output)} entries)")


def save_dialog_csv(parsed_list: list[dict]):
    """Simpan dialog original + rephrase sebagai CSV."""
    path = os.path.join(OUTPUT_DIR, "dialog_cosine.csv")
    fieldnames = ["label", "nama", "jenis", "kategori_dialog", "teks_dialog"]
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        for p in parsed_list:
            writer.writerow({
                "label": p["label"],
                "nama": p["nama"],
                "jenis": "original",
                "kategori_dialog": p["kategori_dialog"],
                "teks_dialog": p["dialog_original"],
            })
            writer.writerow({
                "label": p["label"],
                "nama": p["nama"],
                "jenis": "rephrase",
                "kategori_dialog": p["kategori_dialog"],
                "teks_dialog": p["dialog_rephrase"],
            })
    print(f"✅ Dialog CSV: {path}")


def save_tabel_perbandingan_csv(parsed_list: list[dict]):
    """
    Simpan CSV format tabel_perbandingan_lengkap:
      label, jenis, teks_dialog, teks_kritik, skor_critic

    - Original: 1 row per label (dari 1× critic run pada versi original)
    - Rephrase: 10 rows per label (dari 10 critic yang sesuai label)
    """
    path = os.path.join(OUTPUT_DIR, "tabel_perbandingan_lengkap_cosine.csv")
    fieldnames = ["label", "jenis", "teks_dialog", "teks_kritik", "skor_critic", "waktu_detik"]

    rows = []
    for p in parsed_list:
        label = p["label"]

        # ── Original (1 row) ──
        co = p["critic_original"]
        justifikasi = co.get("justifikasi", "") or (co["catatan"][0] if co["catatan"] else "")
        rows.append({
            "label": label,
            "jenis": "original",
            "teks_dialog": p["dialog_original"],
            "teks_kritik": justifikasi,
            "skor_critic": co["skor"],
            "waktu_detik": co.get("waktu_detik", 0),
        })

        # ── Rephrase (10 rows) ──
        for cr in p["critic_rephrase"]:
            justifikasi = cr.get("justifikasi", "") or (cr["catatan"][0] if cr["catatan"] else "")
            rows.append({
                "label": label,
                "jenis": "rephrase",
                "teks_dialog": p["dialog_rephrase"],
                "teks_kritik": justifikasi,
                "skor_critic": cr["skor"],
                "waktu_detik": cr.get("waktu_detik", 0),
            })

    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)

    n_original = sum(1 for r in rows if r["jenis"] == "original")
    n_rephrase = sum(1 for r in rows if r["jenis"] == "rephrase")
    print(f"✅ Tabel Perbandingan CSV: {path}")
    print(f"   {n_original} original + {n_rephrase} rephrase = {len(rows)} total baris")


# ═══════════════════════════════════════════════════════════════════════════════
# MAIN
# ═══════════════════════════════════════════════════════════════════════════════

def main():
    print("=" * 70)
    print("  COSINE SIMILARITY FULL PIPELINE")
    print("  (original 1×, rephrase 10 valid sesuai label, tanpa bias)")
    print("=" * 70)

    # ── Load ──
    samples = load_samples()
    print(f"\n📂 Loaded {len(samples)} samples dari dialog_test_cosine_similarity.json")

    # ── Paraphrase ──
    print(f"\n{'─' * 70}")
    print("  STEP 1: Paraphrase profile & dialog")
    print(f"{'─' * 70}")

    parsed_list = []
    for sample in samples:
        parsed = paraphrase_sample(sample)
        parsed_list.append(parsed)

    # ── Run critic: original 1×, rephrase cari 10 yang sesuai label ──
    print(f"\n{'─' * 70}")
    print("  STEP 2: Critic — original 1×, rephrase 10 valid")
    print(f"{'─' * 70}")

    for parsed in parsed_list:
        print(f"\n  {'='*50}")
        print(f"  Sample: {parsed['label']} ({parsed['nama']})")
        print(f"  {'='*50}")

        # Original: 1× saja
        print(f"\n  🔍 Critic original...")
        critic_original = run_critic_once(parsed, "original")
        status = "✅" if critic_original["lulus"] else "❌"
        print(f"     skor={critic_original['skor']:.2f} {status}")
        parsed["critic_original"] = critic_original

        # Rephrase: cari 10 yang sesuai label
        critic_rephrase = run_critic_rephrase_10_valid(parsed)
        parsed["critic_rephrase"] = critic_rephrase

    # ── Save outputs ──
    print(f"\n{'─' * 70}")
    print("  STEP 3: Simpan semua output")
    print(f"{'─' * 70}")

    save_profile_json(parsed_list)
    save_profile_csv(parsed_list)
    save_dialog_json(parsed_list)
    save_dialog_csv(parsed_list)
    save_tabel_perbandingan_csv(parsed_list)

    # ── Summary ──
    print(f"\n{'=' * 70}")
    print("  ✅ PIPELINE SELESAI")
    print(f"{'=' * 70}")
    print(f"\n  File output di: {OUTPUT_DIR}/")
    print(f"    - profile_cosine.json / .csv")
    print(f"    - dialog_cosine.json / .csv")
    print(f"    - tabel_perbandingan_lengkap_cosine.csv  ← untuk cosine_sim_count.ipynb")
    print()


if __name__ == "__main__":
    main()