"""Generate cosine similarity test samples from critic_testing.json."""
import json
import csv
import os

OUTPUT_DIR = os.path.join(os.path.dirname(__file__), "output")

# Load all data
with open(os.path.join(OUTPUT_DIR, "profile_testing.json"), "r", encoding="utf-8") as f:
    profiles = json.load(f)
with open(os.path.join(OUTPUT_DIR, "dialogue_testing.json"), "r", encoding="utf-8") as f:
    dialogues = json.load(f)
with open(os.path.join(OUTPUT_DIR, "critic_testing.json"), "r", encoding="utf-8") as f:
    critics = json.load(f)

# Build profile lookup by (jenis_profile, ke) — ke is implicit position
profile_lookup = {}
counter = {}
for p in profiles:
    jenis = p["jenis_profile"]
    counter.setdefault(jenis, 0)
    counter[jenis] += 1
    key = (jenis, counter[jenis])
    profile_lookup[key] = p

# Build dialogue lookup by (jenis_profile, nama, ke, dialog_state)
dialogue_lookup = {}
for d in dialogues:
    if "dialog_state" not in d:
        continue
    key = (d["jenis_profile"], d["nama"], d["ke"], d["dialog_state"])
    dialogue_lookup[key] = d

# ── Find samples dynamically ──────────────────────────────────────────────────
# Dialog type: curhat (dialog panjang, banyak dimensi → critic komprehensif)
DTYPE = "curhat"

# GOOD: first entry with exactly 1 attempt (lulus)
good_entry = None
for c in critics:
    if c["dialog_state"] == DTYPE and c["critic_lulus"] and len(c["critic_log"]) == 1 and c["critic_skor"] >= 4.5:
        good_entry = c
        break

# BAD: entry with >1 attempt, lowest first-attempt skor
# (ini berarti dialog awalnya jelek, perlu revisi oleh critic)
bad_candidates = sorted(
    [c for c in critics if c["dialog_state"] == DTYPE and len(c["critic_log"]) > 1 and c != good_entry],
    key=lambda c: c["critic_log"][0]["skor"]
)
bad_entry = bad_candidates[0] if bad_candidates else None

print(f"GOOD: {good_entry['nama']} ({good_entry['jenis_profile']}) ke={good_entry['ke']}, skor={good_entry['critic_skor']}, attempts={len(good_entry['critic_log'])}")
print(f"BAD:  {bad_entry['nama']} ({bad_entry['jenis_profile']}) ke={bad_entry['ke']}, skor={bad_entry['critic_skor']}, attempts={len(bad_entry['critic_log'])}")

samples = [
    ("good", good_entry),
    ("bad", bad_entry),
]

json_output = []
csv_rows = []

for label, critic_entry in samples:
    jenis = critic_entry["jenis_profile"]
    nama = critic_entry["nama"]
    ke = critic_entry["ke"]
    dtype = critic_entry["dialog_state"]

    # Find profile (by jenis_profile + ke)
    prof = profile_lookup.get((jenis, ke), {})

    # Find original dialogue
    dial = dialogue_lookup.get((jenis, nama, ke, dtype), {})

    # Build JSON entry — hanya iterasi ke-1
    first_log = critic_entry["critic_log"][0]
    entry_data = {
        "label": label,
        "kategori_dialog": dtype,
        "jenis_profile": jenis,
        "nama": nama,
        "usia": prof.get("usia", ""),
        "gender": prof.get("gender", ""),
        "ke": ke,
        "profil": {
            "background": prof.get("background", ""),
            "masalah_hari_ini": prof.get("masalah_hari_ini", ""),
            "ocean": prof.get("ocean", {}),
            "max_fails": prof.get("max_fails", 0),
            "reaksi_gaya": prof.get("reaksi_gaya", ""),
        },
        "dialog_original": dial.get("dialog_text", ""),
        "critic_skor": first_log["skor"],
        "critic_lulus": first_log["lulus"],
        "total_attempts": len(critic_entry["critic_log"]),
        "critic_skor_dimensi": first_log["skor_dimensi"],
        "critic_catatan": first_log["catatan"],
        "critic_justifikasi": first_log.get("justifikasi", ""),
        "critic_saran": first_log.get("saran", ""),
        "teks_dievaluasi": first_log.get("teks_dievaluasi", dial.get("dialog_text", "")),
    }
    json_output.append(entry_data)

    # CSV: hanya iterasi ke-1
    log = critic_entry["critic_log"][0]
    csv_rows.append({
        "label": label,
        "kategori_dialog": dtype,
        "jenis_profile": jenis,
        "nama": nama,
        "iterasi_ke": log["attempt_ke"],
        "teks_dialog": log.get("teks_dievaluasi", dial.get("dialog_text", "")),
        "teks_kritik": log.get("justifikasi", "") or (log["catatan"][0] if log["catatan"] else ""),
        "skor_kritik": log["skor"],
    })

# ── Save JSON ─────────────────────────────────────────────────────────────────
json_path = os.path.join(OUTPUT_DIR, "dialog_test_cosine_similarity.json")
with open(json_path, "w", encoding="utf-8") as f:
    json.dump(json_output, f, ensure_ascii=False, indent=2)
print(f"\nJSON: {json_path} ({len(json_output)} samples)")

# ── Save CSV ──────────────────────────────────────────────────────────────────
csv_path = os.path.join(OUTPUT_DIR, "dialog_test_cosine_similarity.csv")
fieldnames = ["label", "kategori_dialog", "jenis_profile", "nama", "iterasi_ke", "teks_dialog", "teks_kritik", "skor_kritik"]
with open(csv_path, "w", encoding="utf-8-sig", newline="") as f:
    writer = csv.DictWriter(f, fieldnames=fieldnames)
    writer.writeheader()
    writer.writerows(csv_rows)
print(f"CSV: {csv_path} ({len(csv_rows)} rows)")

# ── Print summary ─────────────────────────────────────────────────────────────
for e in json_output:
    print(f"\n{'='*60}")
    print(f"  [{e['label'].upper()}] {e['nama']} ({e['jenis_profile']}) — {e['kategori_dialog']}")
    print(f"  Skor: {e['critic_skor']} | Lulus: {e['critic_lulus']} | Total Attempts: {e['total_attempts']}")
    print(f"  OCEAN: {e['profil']['ocean']}")
    print(f"  Teks: {e['teks_dievaluasi'][:150]}...")
    print(f"  Kritik: {e['critic_catatan'][0][:150] if e['critic_catatan'] else 'N/A'}...")

# ── Save JSON ─────────────────────────────────────────────────────────────────
json_path = os.path.join(OUTPUT_DIR, "dialog_test_cosine_similarity.json")
with open(json_path, "w", encoding="utf-8") as f:
    json.dump(json_output, f, ensure_ascii=False, indent=2)
print(f"JSON: {json_path} ({len(json_output)} samples)")

# ── Save CSV ──────────────────────────────────────────────────────────────────
csv_path = os.path.join(OUTPUT_DIR, "dialog_test_cosine_similarity.csv")
fieldnames = ["label", "kategori_dialog", "jenis_profile", "nama", "iterasi_ke", "teks_dialog", "teks_kritik", "skor_kritik"]
with open(csv_path, "w", encoding="utf-8-sig", newline="") as f:
    writer = csv.DictWriter(f, fieldnames=fieldnames)
    writer.writeheader()
    writer.writerows(csv_rows)
print(f"CSV: {csv_path} ({len(csv_rows)} rows)")

# ── Print summary ─────────────────────────────────────────────────────────────
for e in json_output:
    print(f"\n{'='*60}")
    print(f"  [{e['label'].upper()}] {e['nama']} ({e['jenis_profile']}) — {e['kategori_dialog']}")
    print(f"  Skor: {e['critic_skor']} | Lulus: {e['critic_lulus']} | Attempts: {e['total_attempts']}")
    print(f"  OCEAN: {e['profil']['ocean']}")
    print(f"  Dialog: {e['dialog_original'][:120]}...")