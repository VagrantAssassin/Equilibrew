"""
generate_tabel_perbandingan.py
------------------------------
Helper: generate tabel CSV (label | teks | skor_critic) dari results.json.

Membaca exp_critic_consistency/results.json yang sudah ada, lalu menulis:
  exp_critic_consistency/tabel_perbandingan.csv

Struktur tabel:
  - 1 baris untuk dialog DASAR (original) per label
  - 1 baris untuk tiap iterasi (input rephrase, frozen)

Teks original & rephrase di-hardcode dari seed (Tabel 4.2/4.3), karena di
results.json versi lama field teks belum disimpan.

Cara pakai:
  cd "d:\\File Unity\\Equilibrew\\Multi Agent"
  python generate_tabel_perbandingan.py
"""

import csv
import json
import os

OUT_DIR = "exp_critic_consistency"
RESULTS_PATH = os.path.join(OUT_DIR, "results.json")
CSV_PATH = os.path.join(OUT_DIR, "tabel_perbandingan.csv")

# Teks dialog (dari seed, Tabel 4.3). Dipakai untuk mengisi kolom "teks".
ORIGINAL_TEXT = {
    "good": (
        "Eh kak, akhirnya sampai juga! Pesen Green Tea ya kak, soalnya kepala "
        "aku penuh banget tadi mulai dari deadline tugas kelompok yang dimajuin "
        "besok sampe anggota tim yang pada ngilang. Serius deh, butuh teh buat "
        "ngecabin malam nanti. Makasih ya kak!"
    ),
    "bad": "Saya ingin memesan green tea, cepat ya saya buru buru.",
}

REPHRASE_TEXT = {
    "good": (
        "Eh kak, akhirnya tiba juga! Aku pesan Green Tea ya kak, karena kepala "
        "lagi penuh banget, dari deadline tugas kelompok yang dimajuin jadi besok "
        "sampai anggota tim yang semuanya menghilang. Beneran deh, aku perlu teh "
        "buat ngelewatin malam nanti. Makasih ya kak!"
    ),
    "bad": "Saya mau memesan green tea, cepat ya, saya sedang buru-buru.",
}


def main() -> int:
    with open(RESULTS_PATH, encoding="utf-8") as f:
        data = json.load(f)

    # Membuat tabel dengan 2 versi:
    # 1. Versi lama: label, jenis, teks (dialog input), skor_critic
    # 2. Versi baru: label, jenis, teks_dialog, teks_kritik, skor_critic
    
    rows_v1 = []  # Tabel lama (hanya teks dialog)
    rows_v2 = []  # Tabel baru (dialog + kritik)
    
    for res in data:
        label = res["label"]

        # Referensi (dialog dasar) — support nama field lama "reference"
        # maupun baru "original".
        ref = res.get("original") or res.get("reference")
        
        # Versi 1: hanya teks dialog
        rows_v1.append({
            "label": label,
            "jenis": "original",
            "teks": res.get("teks") or ORIGINAL_TEXT[label],
            "skor_critic": ref["skor_total"],
        })
        
        # Versi 2: dialog + kritik
        rows_v2.append({
            "label": label,
            "jenis": "original",
            "teks_dialog": res.get("teks") or ORIGINAL_TEXT[label],
            "teks_kritik": ref.get("justifikasi", ""),
            "skor_critic": ref["skor_total"],
        })

        # Tiap iterasi rephrase (teks frozen, sama semua)
        for rec in res.get("iterations", []):
            # Versi 1: hanya teks dialog
            rows_v1.append({
                "label": label,
                "jenis": "rephrase",
                "teks": rec.get("teks") or REPHRASE_TEXT[label],
                "skor_critic": rec["skor_critic"],
            })
            
            # Versi 2: dialog + kritik
            rows_v2.append({
                "label": label,
                "jenis": "rephrase",
                "teks_dialog": rec.get("teks") or REPHRASE_TEXT[label],
                "teks_kritik": rec.get("output_teks", ""),
                "skor_critic": rec["skor_critic"],
            })

    # Save versi 1 (tabel lama kompatibel)
    with open(CSV_PATH, "w", encoding="utf-8", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["label", "jenis", "teks", "skor_critic"])
        for r in rows_v1:
            writer.writerow([r["label"], r["jenis"], r["teks"], r["skor_critic"]])
    
    print(f"✓ Tabel lama disimpan: {os.path.abspath(CSV_PATH)}")
    print(f"  Total baris: {len(rows_v1)} (di luar header)")
    
    # Save versi 2 (tabel baru dengan teks kritik)
    csv_v2_path = os.path.join(OUT_DIR, "tabel_perbandingan_lengkap.csv")
    with open(csv_v2_path, "w", encoding="utf-8", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["label", "jenis", "teks_dialog", "teks_kritik", "skor_critic"])
        for r in rows_v2:
            writer.writerow([r["label"], r["jenis"], r["teks_dialog"], 
                           r["teks_kritik"], r["skor_critic"]])
    
    print(f"✓ Tabel lengkap (dengan teks kritik) disimpan: {os.path.abspath(csv_v2_path)}")
    print(f"  Total baris: {len(rows_v2)} (di luar header)")
    
    # Cek apakah ada teks kritik kosong
    empty_critiques = sum(1 for r in rows_v2 if not r["teks_kritik"].strip())
    if empty_critiques:
        print(f"⚠  PERHATIAN: {empty_critiques} baris memiliki teks kritik kosong")
    
    return 0


if __name__ == "__main__":
    raise SystemExit(main())