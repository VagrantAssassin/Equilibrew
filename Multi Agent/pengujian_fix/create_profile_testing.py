"""
create_profile_testing.py
=========================
Membuat 10 profil untuk setiap kategori (usia × gender) menggunakan Profile Agent.
Total: 6 kategori × 10 profil = 60 profil.

Output:
  - output/profile_testing.json  → array murni profil (tanpa metadata iterasi)
  - output/profile_testing.csv   → pemetaan flat dari JSON
"""

import sys
import os
import json
import csv
import time

# Tambahkan parent folder (Multi Agent) ke path agar bisa import agents
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from agents.profile_agent import run as profile_run

# ── Konfigurasi ───────────────────────────────────────────────────────────────
OUTPUT_DIR = os.path.join(os.path.dirname(__file__), "output")
os.makedirs(OUTPUT_DIR, exist_ok=True)

KATEGORI = [
    ("remaja", "pria"),
    ("remaja", "wanita"),
    ("dewasa", "pria"),
    ("dewasa", "wanita"),
    ("orang tua", "pria"),
    ("orang tua", "wanita"),
]

JUMLAH_PER_KATEGORI = 10

# ── Main ──────────────────────────────────────────────────────────────────────

def main():
    semua_profil = []  # untuk JSON
    csv_rows = []      # untuk CSV

    for usia, gender in KATEGORI:
        jenis_profile = f"{usia} {gender}"
        print(f"\n{'='*60}")
        print(f"  Generate profil: {jenis_profile} ({JUMLAH_PER_KATEGORI} profil)")
        print(f"{'='*60}")

        for i in range(1, JUMLAH_PER_KATEGORI + 1):
            state = {"usia": usia, "gender": gender}
            print(f"  [{i}/{JUMLAH_PER_KATEGORI}] Memanggil Profile Agent...", end=" ")

            try:
                t_start = time.perf_counter()
                result = profile_run(state)
                t_end = time.perf_counter()
                waktu_detik = round(t_end - t_start, 3)
            except Exception as e:
                print(f"ERROR: {e}")
                continue

            # Profile agent return: nama, background, masalah_hari_ini, ocean, max_fails, reaksi_gaya
            nama = result.get("nama", "")
            background = result.get("background", "")
            masalah = result.get("masalah_hari_ini", "")
            ocean = result.get("ocean", {})
            max_fails = result.get("max_fails", 2)
            reaksi_gaya = result.get("reaksi_gaya", "")

            profil = {
                "jenis_profile": jenis_profile,
                "nama": nama,
                "usia": usia,
                "gender": gender,
                "background": background,
                "masalah_hari_ini": masalah,
                "ocean": ocean,
                "max_fails": max_fails,
                "reaksi_gaya": reaksi_gaya,
            }

            semua_profil.append(profil)

            csv_rows.append({
                "jenis_profile": jenis_profile,
                "nama": nama,
                "usia": usia,
                "gender": gender,
                "background": background,
                "masalah_hari_ini": masalah,
                "ocean_O": ocean.get("openness", 0),
                "ocean_C": ocean.get("conscientiousness", 0),
                "ocean_E": ocean.get("extraversion", 0),
                "ocean_A": ocean.get("agreeableness", 0),
                "ocean_N": ocean.get("neuroticism", 0),
                "max_fails": max_fails,
                "reaksi_gaya": reaksi_gaya,
                "waktu_detik": waktu_detik,
            })

            print(f"OK → {nama} ({waktu_detik:.2f}s)")

    # ── Simpan JSON ───────────────────────────────────────────────────────────
    json_path = os.path.join(OUTPUT_DIR, "profile_testing.json")
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(semua_profil, f, ensure_ascii=False, indent=2)
    print(f"\n✅ JSON tersimpan: {json_path} ({len(semua_profil)} profil)")

    # ── Simpan CSV ────────────────────────────────────────────────────────────
    csv_path = os.path.join(OUTPUT_DIR, "profile_testing.csv")
    fieldnames = [
        "jenis_profile", "nama", "usia", "gender",
        "background", "masalah_hari_ini",
        "ocean_O", "ocean_C", "ocean_E", "ocean_A", "ocean_N",
        "max_fails", "reaksi_gaya", "waktu_detik",
    ]
    with open(csv_path, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(csv_rows)
    print(f"✅ CSV tersimpan: {csv_path} ({len(csv_rows)} baris)")


if __name__ == "__main__":
    main()