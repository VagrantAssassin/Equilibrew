"""
critic_test.py
==============
Menguji iterasi Actor-Critic: menilai setiap dialog dari dialogue_testing.json
menggunakan Critic Agent dengan loop revisi.

Loop: max 10 attempt (1 awal + 9 revisi), early break jika skor >= CRITIC_THRESHOLD.

Membaca dialogue_testing.json dalam format API response (DialogResponse / EvaluateResponse).

Output:
  - output/critic_testing.json  → flat array per entry:
                                   {jenis_profile, nama, usia, gender, dialog_state, ke,
                                    dialog_teks, critic_skor, critic_lulus,
                                    critic_skor_dimensi, critic_catatan, critic_saran,
                                    critic_log: [{attempt_ke, skor, skor_dimensi, saran, catatan, lulus}]}
  - output/critic_testing.csv   → flat: jenis_profile, nama_profile, iterasi_ke, jenis_dialog, teks_dialog, teks_kritik, skor_kritik
"""

import sys
import os
import json
import csv
import time
from collections import OrderedDict

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from config import CRITIC_THRESHOLD

# ── Override: maksimal 10 attempt total (1 awal + 9 revisi) ───────────────────
CRITIC_MAX_ATTEMPT = 10

from agents.critic_agent import run as critic_run
from agents.dialogue_agent import (
    run_pesanan,
    run_pesanan_salah,
    run_marah,
    run_berhasil,
    run_curhat,
    run_reaksi,
    run_closing,
)

# ── Konfigurasi ───────────────────────────────────────────────────────────────
OUTPUT_DIR = os.path.join(os.path.dirname(__file__), "output")
os.makedirs(OUTPUT_DIR, exist_ok=True)

DIALOGUE_JSON = os.path.join(OUTPUT_DIR, "dialogue_testing.json")
PROFILE_JSON  = os.path.join(OUTPUT_DIR, "profile_testing.json")

# ── Mapping dialog_state → konteks, field, dan fungsi revisi ──────────────────

DIALOG_TYPE_MAP = {
    "pesanan": {
        "konteks": "pesanan",
        "field": "dialog_pesanan",
        "revisi_fn": run_pesanan,
    },
    "pesanan_salah": {
        "konteks": "pesanan_salah",
        "field": "dialog_pesanan_salah",
        "revisi_fn": run_pesanan_salah,
    },
    "marah": {
        "konteks": "marah",
        "field": "dialog_marah",
        "revisi_fn": run_marah,
    },
    "berhasil": {
        "konteks": "berhasil",
        "field": "dialog_berhasil",
        "revisi_fn": run_berhasil,
    },
    "curhat": {
        "konteks": "curhat",
        "field": "dialog_npc",
        "revisi_fn": run_curhat,
    },
    "reaksi": {
        "konteks": "reaksi",
        "field": "reaksi_npc",
        "revisi_fn": run_reaksi,
    },
    "closing": {
        "konteks": "closing",
        "field": "dialog_closing",
        "revisi_fn": run_closing,
    },
}


def load_profiles():
    with open(PROFILE_JSON, "r", encoding="utf-8") as f:
        return json.load(f)


def load_dialogues():
    """Load dialogue_testing.json (flat array dengan API format)."""
    with open(DIALOGUE_JSON, "r", encoding="utf-8") as f:
        return json.load(f)


def build_profile_index(profiles: list) -> dict:
    """Buat lookup profil berdasarkan indeks (jenis_profile, ke-1..10)."""
    idx = {}
    counter = {}
    for p in profiles:
        jenis = p["jenis_profile"]
        counter.setdefault(jenis, 0)
        counter[jenis] += 1
        key = (jenis, counter[jenis])
        idx[key] = p
    return idx


def flatten_dialogue_entries(dialogues: list) -> list:
    """Group flat array by (jenis_profile, ke) → setiap group punya 7 dialog.
    Skip entries tanpa dialog_state (error entries) dan entries dengan field error."""
    groups = OrderedDict()
    for entry in dialogues:
        # Skip error entries yang tidak punya dialog_state
        if "dialog_state" not in entry:
            continue
        # Skip entries yang memiliki field error
        if entry.get("error") or entry.get("error_message"):
            print(f"  ⚠️ SKIP error entry: {entry.get('error') or entry.get('error_message')}")
            continue
        key = (entry["jenis_profile"], entry["ke"])
        if key not in groups:
            groups[key] = {
                "jenis_profile": entry["jenis_profile"],
                "nama": entry["nama"],
                "usia": entry["usia"],
                "gender": entry["gender"],
                "ke": entry["ke"],
                "entries": [],
            }
        groups[key]["entries"].append(entry)

    return list(groups.values())


def build_state_for_critic(profil: dict, entry: dict) -> dict:
    """
    Bangun state yang diperlukan oleh critic_agent.run().
    Menggunakan data dari entry (API response) — BUKAN random.
    """
    dialog_type = entry["dialog_state"]
    info = DIALOG_TYPE_MAP[dialog_type]
    dialog_text = entry.get("dialog_text", "") or entry.get("reaksi_npc", "")
    minuman = entry.get("minuman_dipesan", "")

    state = {
        "usia": profil["usia"],
        "gender": profil["gender"],
        "nama": profil["nama"],
        "background": profil["background"],
        "masalah_hari_ini": profil["masalah_hari_ini"],
        "ocean": profil["ocean"],
        "max_fails": profil["max_fails"],
        "reaksi_gaya": profil.get("reaksi_gaya", ""),
        "minuman_dipesan": minuman if minuman else "",
        "mood": 50,
        "fail_count": 1,
        "ronde_sekarang": 1,
        "total_ronde": entry.get("total_ronde", 3),
        "riwayat": [],
        "jawaban_pemain": "Iya kak, aku ngerti kok.",
        "dialog_npc": dialog_text if dialog_type == "curhat" else "",
        "reaksi_npc": dialog_text if dialog_type == "reaksi" else "",
        "konteks_critic": info["konteks"],
        "revisi_ke": 0,
        info["field"]: dialog_text,
    }

    if dialog_type == "curhat":
        state["pilihan_jawaban"] = entry.get("pilihan_jawaban", [])

    # Untuk reaksi, tambahkan riwayat dari entry
    if dialog_type == "reaksi":
        riwayat_entry = entry.get("riwayat_entry", {})
        if riwayat_entry:
            state["riwayat"] = [riwayat_entry]
            state["jawaban_pemain"] = riwayat_entry.get("jawaban_pemain", "")

    return state


def revise_dialog(dialog_type: str, state: dict, saran: str) -> tuple:
    """
    Panggil fungsi revisi dialogue agent.
    Return (field_name, new_dialog_text).
    """
    info = DIALOG_TYPE_MAP[dialog_type]
    fn = info["revisi_fn"]
    field = info["field"]

    result = fn(state, saran_revisi=saran)

    text = result.get(field, "")
    if not text:
        for v in result.values():
            if isinstance(v, str) and len(v) > 3:
                text = v
                break

    return field, text


def actor_critic_loop(profil: dict, entry: dict) -> dict:
    """
    Jalankan actor-critic loop untuk satu dialog.
    """
    dialog_type = entry["dialog_state"]
    info = DIALOG_TYPE_MAP[dialog_type]
    dialog_text = entry.get("dialog_text", "") or entry.get("reaksi_npc", "")
    state = build_state_for_critic(profil, entry)
    current_text = dialog_text

    critic_log = []

    for i in range(CRITIC_MAX_ATTEMPT):       # 0..9 → max 10 attempt
        attempt_ke = i + 1

        # Jalankan critic
        t_start = time.perf_counter()
        try:
            critic_result = critic_run(state)
        except Exception as e:
            print(f"      ERROR critic: {e}")
            critic_result = {
                "critic_lulus": False,
                "critic_skor": 0.0,
                "critic_skor_dimensi": {},
                "critic_catatan": [str(e)],
                "critic_saran": "",
                "revisi_ke": attempt_ke,
            }
        t_end = time.perf_counter()
        waktu_detik = round(t_end - t_start, 3)

        skor = critic_result.get("critic_skor", 0)
        lulus = skor >= CRITIC_THRESHOLD

        # Catat log attempt (teks yang dievaluasi di attempt ini)
        critic_log.append({
            "attempt_ke": attempt_ke,
            "skor": round(skor, 2),
            "skor_dimensi": critic_result.get("critic_skor_dimensi", {}),
            "saran": critic_result.get("critic_saran", ""),
            "catatan": critic_result.get("critic_catatan", []),
            "lulus": lulus,
            "teks_dievaluasi": current_text,
            "waktu_detik": waktu_detik,
        })

        print(f"      attempt {attempt_ke}: skor={skor:.2f} {'✅ LULUS' if lulus else '❌ GAGAL'} ({waktu_detik:.2f}s)")

        if lulus:
            break

        # Revisi dialog
        saran = critic_result.get("critic_saran", "")
        if not saran:
            print(f"      WARNING: Tidak ada saran revisi, skip")
            break

        try:
            field_name, new_text = revise_dialog(dialog_type, state, saran)
        except Exception as e:
            print(f"      ERROR revisi dialog: {e}")
            break

        if not new_text or new_text == current_text:
            print(f"      WARNING: Revisi tidak menghasilkan perubahan")
            break

        current_text = new_text
        state[field_name] = new_text
        state["revisi_ke"] = attempt_ke

    final_log = critic_log[-1] if critic_log else {}

    return {
        "critic_skor": final_log.get("skor", 0),
        "critic_lulus": final_log.get("lulus", False),
        "critic_skor_dimensi": final_log.get("skor_dimensi", {}),
        "critic_catatan": final_log.get("catatan", []),
        "critic_saran": final_log.get("saran", ""),
        "critic_log": critic_log,
        "dialog_teks_akhir": current_text,
    }


# ── Main ──────────────────────────────────────────────────────────────────────

def main():
    profiles = load_profiles()
    dialogues = load_dialogues()

    groups = flatten_dialogue_entries(dialogues)

    print(f"Loaded {len(profiles)} profiles")
    print(f"Loaded {len(groups)} profile groups (masing-masing 7 dialog)")

    profil_idx = build_profile_index(profiles)

    json_output = []
    csv_rows = []

    for g_idx, group in enumerate(groups):
        jenis = group["jenis_profile"]
        nama = group["nama"]
        ke = group["ke"]

        # Cari profil lengkap dengan (jenis, ke)
        key = (jenis, ke)
        profil = profil_idx.get(key)
        if not profil:
            print(f"  WARNING: Profil tidak ditemukan untuk {key}")
            continue

        print(f"\n{'='*60}")
        print(f"  [{g_idx+1}/{len(groups)}] {jenis} — {nama} (ke-{ke})")
        print(f"{'='*60}")

        # Urutkan entries: pesanan, pesanan_salah, marah, berhasil, curhat, reaksi, closing
        state_order = ["pesanan", "pesanan_salah", "marah", "berhasil", "curhat", "reaksi", "closing"]
        ordered_entries = sorted(
            group["entries"],
            key=lambda e: state_order.index(e["dialog_state"]) if e["dialog_state"] in state_order else 999,
        )

        for entry in ordered_entries:
            dtype = entry["dialog_state"]

            # Extract teks dialog dari API format (dialog_text atau reaksi_npc)
            dialog_text = entry.get("dialog_text", "") or entry.get("reaksi_npc", "")

            if not dialog_text:
                print(f"  [{dtype}] SKIP — tidak ada teks dialog")
                continue

            print(f"  [{dtype}] ", end="", flush=True)
            try:
                result = actor_critic_loop(profil, entry)
            except Exception as e:
                print(f"\n  ⚠️ [{dtype}] SKIP — error pada actor_critic_loop: {e}")
                continue

            # ── JSON entry ───────────────────────────────────────────────────
            json_output.append({
                "jenis_profile": jenis,
                "nama": nama,
                "usia": group["usia"],
                "gender": group["gender"],
                "dialog_state": dtype,
                "ke": ke,
                "dialog_teks": dialog_text,
                "dialog_teks_akhir": result["dialog_teks_akhir"],
                "critic_skor": result["critic_skor"],
                "critic_lulus": result["critic_lulus"],
                "critic_skor_dimensi": result["critic_skor_dimensi"],
                "critic_catatan": result["critic_catatan"],
                "critic_saran": result["critic_saran"],
                "critic_log": result["critic_log"],
            })

            # ── CSV: SATU BARIS PER ATTEMPT ──────────────────────────────────
            for log in result["critic_log"]:
                csv_rows.append({
                    "jenis_profile": jenis,
                    "nama_profile": nama,
                    "iterasi_ke": log["attempt_ke"],
                    "jenis_dialog": dtype,
                    "teks_dialog": log.get("teks_dievaluasi", dialog_text),
                    "teks_kritik": log["catatan"][0] if log["catatan"] else "",
                    "skor_kritik": log["skor"],
                    "waktu_detik": log.get("waktu_detik", 0),
                })

    # ── Simpan JSON ───────────────────────────────────────────────────────────
    json_path = os.path.join(OUTPUT_DIR, "critic_testing.json")
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(json_output, f, ensure_ascii=False, indent=2)
    print(f"\n✅ JSON tersimpan: {json_path} ({len(json_output)} entries)")

    # ── Simpan CSV ────────────────────────────────────────────────────────────
    csv_path = os.path.join(OUTPUT_DIR, "critic_testing.csv")
    fieldnames = ["jenis_profile", "nama_profile", "iterasi_ke", "jenis_dialog", "teks_dialog", "teks_kritik", "skor_kritik", "waktu_detik"]
    with open(csv_path, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(csv_rows)
    print(f"✅ CSV tersimpan: {csv_path} ({len(csv_rows)} baris)")


if __name__ == "__main__":
    main()