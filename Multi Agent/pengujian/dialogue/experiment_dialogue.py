"""
Eksperimen Dialogue Agent — Batch.
====================================
Menghasilkan: 6 profil × 5 state × 10 panggilan = 300 dialog.

Cara pakai:
  1. Pastikan server.py berjalan: python server.py
  2. Jalankan script ini:    python experiment_dialogue.py

Output: exp_dialogue/ (folder berisi JSON per state, dan summary CSV)
"""

import requests
import json
import os
import time
from datetime import datetime

API = "http://localhost:8765"

# ── 6 variasi profil (3 usia × 2 gender) ──────────────────────────────────────
VARIATIONS = [
    {"usia": "anak", "gender": "pria"},
    {"usia": "anak", "gender": "wanita"},
    {"usia": "remaja", "gender": "pria"},
    {"usia": "remaja", "gender": "wanita"},
    {"usia": "dewasa", "gender": "pria"},
    {"usia": "dewasa", "gender": "wanita"},
]

STATES = ["pesanan", "pesanan_salah", "marah", "berhasil", "curhat"]
JUMLAH_PER = 10  # jumlah panggilan per state per profil

OUT_DIR = "exp_dialogue"
os.makedirs(OUT_DIR, exist_ok=True)


def call_dialogue(usia: str, gender: str, state: str) -> dict:
    """Panggil /test/dialogue, return full response JSON."""
    payload = {"usia": usia, "gender": gender, "state": state}
    r = requests.post(f"{API}/test/dialogue", json=payload, timeout=120)
    r.raise_for_status()
    return r.json()


def main():
    print("=" * 60)
    print("DIALOGUE AGENT EXPERIMENT")
    print(f"Variasi: {len(VARIATIONS)} profil")
    print(f"States:  {len(STATES)} ({', '.join(STATES)})")
    print(f"Per state: {JUMLAH_PER} calls")
    print(f"Total:    {len(VARIATIONS) * len(STATES) * JUMLAH_PER} dialog")
    print("=" * 60)

    all_results = {}  # {state: [dia1, dia2, ...]}

    for state in STATES:
        print(f"\n{'─' * 40}")
        print(f"STATE: {state}")
        state_results = []

        for var in VARIATIONS:
            label = f"{var['usia']}_{var['gender']}"
            print(f"  [{label}] ", end="", flush=True)

            for i in range(JUMLAH_PER):
                try:
                    resp = call_dialogue(var["usia"], var["gender"], state)
                    state_results.append({
                        "usia": var["usia"],
                        "gender": var["gender"],
                        "state": state,
                        "ke": i + 1,
                        "dialog": resp.get("dialog", {}),
                    })
                    print(".", end="", flush=True)
                except Exception as e:
                    print(f"✗(call {i+1}: {e})", end="", flush=True)
                    state_results.append({
                        "usia": var["usia"],
                        "gender": var["gender"],
                        "state": state,
                        "ke": i + 1,
                        "error": str(e),
                    })

                time.sleep(0.3)  # throttle kecil biar gak nge-spam LLM

            print(f" ✓ ({JUMLAH_PER} calls)")

        all_results[state] = state_results

        # Simpan per state
        fname = os.path.join(OUT_DIR, f"dialogue_{state}.json")
        with open(fname, "w", encoding="utf-8") as f:
            json.dump(state_results, f, indent=2, ensure_ascii=False)
        print(f"  → saved: {fname} ({len(state_results)} entries)")

    # ── Summary CSV ──────────────────────────────────────────────────────────
    csv_path = os.path.join(OUT_DIR, "summary.csv")
    with open(csv_path, "w", encoding="utf-8") as f:
        f.write("usia,gender,state,ke,field,value\n")
        for state, entries in all_results.items():
            for entry in entries:
                dialog = entry.get("dialog", {})
                for key, val in dialog.items():
                    if isinstance(val, str):
                        # Escape quotes & newlines for CSV
                        clean = val.replace('"', '""').replace("\n", "\\n")
                        f.write(f'{entry["usia"]},{entry["gender"]},{state},{entry["ke"]},{key},"{clean}"\n')

    print(f"\n✓ Summary CSV: {csv_path}")

    # ── Quick stats ──────────────────────────────────────────────────────────
    total = sum(len(v) for v in all_results.values())
    errors = sum(1 for v in all_results.values() for e in v if "error" in e)
    print(f"\nTotal dialog: {total}")
    print(f"Errors:      {errors}")
    print(f"Output dir:  {os.path.abspath(OUT_DIR)}")


if __name__ == "__main__":
    main()