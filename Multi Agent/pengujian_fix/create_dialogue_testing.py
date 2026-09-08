"""
create_dialogue_testing.py
==========================
Membuat dialog untuk setiap profil (dari profile_testing.json) × 7 jenis dialog.
Diproses secara BERURUTAN: selesaikan semua dialog untuk satu profil,
baru lanjut ke profil berikutnya.

Urutan: remaja pria → remaja wanita → dewasa pria → dewasa wanita → ortu pria → ortu wanita

Output:
  - output/dialogue_testing.json  → flat array per profil × jenis dialog.
    Format MENGIKUTI API response (DialogResponse / EvaluateResponse dari server.py):
      pesanan:       {session_id, dialog_state, dialog_text, minuman_dipesan, pilihan_jawaban, total_ronde, ronde_sekarang}
      pesanan_salah: {session_id, dialog_state, dialog_text, minuman_dipesan, pilihan_jawaban, total_ronde, ronde_sekarang}
      marah:         {session_id, dialog_state, dialog_text, minuman_dipesan, pilihan_jawaban, total_ronde, ronde_sekarang}
      berhasil:      {session_id, dialog_state, dialog_text, minuman_dipesan, pilihan_jawaban, total_ronde, ronde_sekarang}
      curhat:        {session_id, dialog_state, dialog_text, minuman_dipesan, pilihan_jawaban: [{id,teks,nada}], total_ronde, ronde_sekarang}
      reaksi:        {session_id, nada, mood_sebelum, mood_sesudah, reaksi_npc, riwayat_entry, total_ronde, ronde_sekarang}
      closing:       {session_id, dialog_state, dialog_text, minuman_dipesan, pilihan_jawaban, total_ronde, ronde_sekarang}
    + metadata: {jenis_profile, nama, usia, gender, ke}
  - output/dialogue_testing.csv  → kolom: jenis_profile, nama_profile, jenis_dialog, teks_dialog,
                                    pilihan_jawaban, minuman_dipesan, jawaban_dipilih
"""

import sys
import os
import json
import csv
import uuid
import random
import time

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from config import MENU_MINUMAN
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

PROFILE_JSON = os.path.join(OUTPUT_DIR, "profile_testing.json")

DIALOG_TYPES = ["pesanan", "pesanan_salah", "marah", "berhasil", "curhat", "reaksi", "closing"]

TOTAL_RONDE_DEFAULT = 3


def load_profiles():
    with open(PROFILE_JSON, "r", encoding="utf-8") as f:
        return json.load(f)


def build_base_state(profil: dict) -> dict:
    """Bangun state dasar dari profil untuk semua dialog."""
    return {
        "usia": profil["usia"],
        "gender": profil["gender"],
        "nama": profil["nama"],
        "background": profil["background"],
        "masalah_hari_ini": profil["masalah_hari_ini"],
        "ocean": profil["ocean"],
        "max_fails": profil["max_fails"],
        "reaksi_gaya": profil.get("reaksi_gaya", ""),
        "minuman_dipesan": random.choice(MENU_MINUMAN),
        "mood": 50,
        "fail_count": 0,
        "ronde_sekarang": 1,
        "total_ronde": TOTAL_RONDE_DEFAULT,
        "riwayat": [],
        "jawaban_pemain": "",
        "dialog_npc": "",
        "revisi_ke": 0,
    }


def generate_dialog(dialog_type: str, state: dict) -> dict:
    """
    Panggil fungsi dialogue agent yang sesuai.
    Kembalikan dict dalam format API response (DialogResponse / EvaluateResponse).
    Return {} jika gagal.
    """
    session_id = str(uuid.uuid4())[:8]

    try:
        # ── PESANAN ──────────────────────────────────────────────────────────
        if dialog_type == "pesanan":
            result = run_pesanan(state)
            minuman = result.get("minuman_dipesan", "")
            # Update state agar minuman_dipesan konsisten
            state["minuman_dipesan"] = minuman
            return {
                "session_id": session_id,
                "dialog_state": "pesanan",
                "dialog_text": result.get("dialog_pesanan", ""),
                "minuman_dipesan": minuman,
                "pilihan_jawaban": None,
                "total_ronde": state["total_ronde"],
                "ronde_sekarang": None,
            }

        # ── PESANAN SALAH ────────────────────────────────────────────────────
        elif dialog_type == "pesanan_salah":
            result = run_pesanan_salah(state)
            return {
                "session_id": session_id,
                "dialog_state": "pesanan_salah",
                "dialog_text": result.get("dialog_pesanan_salah", ""),
                "minuman_dipesan": "",
                "pilihan_jawaban": "",
                "total_ronde": state["total_ronde"],
                "ronde_sekarang": "",
            }

        # ── MARAH ────────────────────────────────────────────────────────────
        elif dialog_type == "marah":
            result = run_marah(state)
            return {
                "session_id": session_id,
                "dialog_state": "marah",
                "dialog_text": result.get("dialog_marah", ""),
                "minuman_dipesan": "",
                "pilihan_jawaban": "",
                "total_ronde": state["total_ronde"],
                "ronde_sekarang": "",
            }

        # ── BERHASIL ─────────────────────────────────────────────────────────
        elif dialog_type == "berhasil":
            result = run_berhasil(state)
            return {
                "session_id": session_id,
                "dialog_state": "berhasil",
                "dialog_text": result.get("dialog_berhasil", ""),
                "minuman_dipesan": "",
                "pilihan_jawaban": "",
                "total_ronde": state["total_ronde"],
                "ronde_sekarang": "",
            }

        # ── CURHAT ───────────────────────────────────────────────────────────
        elif dialog_type == "curhat":
            state["ronde_sekarang"] = 1
            state["riwayat"] = []
            result = run_curhat(state)
            # Simpan hasil curhat ke state untuk dipakai reaksi & closing
            state["dialog_npc"] = result.get("dialog_npc", "")
            state["pilihan_jawaban"] = result.get("pilihan_jawaban", [])
            return {
                "session_id": session_id,
                "dialog_state": "curhat",
                "dialog_text": state["dialog_npc"],
                "minuman_dipesan": "",
                "pilihan_jawaban": state["pilihan_jawaban"],
                "total_ronde": state["total_ronde"],
                "ronde_sekarang": 1,
            }

        # ── REAKSI ───────────────────────────────────────────────────────────
        elif dialog_type == "reaksi":
            pilihan = state.get("pilihan_jawaban", [])
            if pilihan:
                jawaban_dipilih = random.choice(pilihan)
            else:
                jawaban_dipilih = {"id": 0, "teks": "Iya kak, aku ngerti kok. Cerita aja, aku dengerin.", "nada": "satisfy"}

            state["ronde_sekarang"] = 1
            state["jawaban_pemain"] = jawaban_dipilih["teks"]
            state["riwayat"] = [{
                "ronde": 1,
                "dialog_npc": state.get("dialog_npc", ""),
                "jawaban_pemain": jawaban_dipilih["teks"],
                "nada": jawaban_dipilih["nada"],
                "mood_sebelum": 50,
                "mood_sesudah": 60,
            }]
            result = run_reaksi(state)
            state["reaksi_npc"] = result.get("reaksi_npc", "")
            state["jawaban_dipilih"] = jawaban_dipilih  # simpan untuk CSV

            tema = state.get("tema", "")
            return {
                "dialog_state": "reaksi",  # metadata untuk critic_test / lookup
                "session_id": session_id,
                "nada": jawaban_dipilih["nada"],
                "mood_sebelum": 50,
                "mood_sesudah": 60,
                "reaksi_npc": state["reaksi_npc"],
                "riwayat_entry": {
                    "ronde": 1,
                    "dialog_npc": state.get("dialog_npc", ""),
                    "jawaban_pemain": jawaban_dipilih["teks"],
                    "nada": jawaban_dipilih["nada"],
                    "mood_sebelum": 50,
                    "mood_sesudah": 60,
                    "reaksi_npc": state["reaksi_npc"],
                    "tema": tema,
                },
                "total_ronde": state["total_ronde"],
                "ronde_sekarang": 1,
            }

        # ── CLOSING ──────────────────────────────────────────────────────────
        elif dialog_type == "closing":
            state["mood"] = 60
            state["riwayat"] = [
                {
                    "ronde": 1,
                    "dialog_npc": state.get("dialog_npc", ""),
                    "jawaban_pemain": state.get("jawaban_pemain", "Iya kak, aku ngerti kok."),
                    "nada": "satisfy",
                    "mood_sebelum": 50,
                    "mood_sesudah": 60,
                    "reaksi_npc": state.get("reaksi_npc", ""),
                }
            ]
            result = run_closing(state)
            return {
                "session_id": session_id,
                "dialog_state": "closing",
                "dialog_text": result.get("dialog_closing", ""),
                "minuman_dipesan": "",
                "pilihan_jawaban": "pilihan n",
                "total_ronde": state["total_ronde"],
                "ronde_sekarang": "",
            }

        else:
            print(f"    WARNING: Unknown dialog type '{dialog_type}'")
            return {}

    except Exception as e:
        print(f"    ERROR: {e}")
        return {"error": str(e)}


# ── Main ──────────────────────────────────────────────────────────────────────

def main():
    profiles = load_profiles()
    print(f"Loaded {len(profiles)} profiles from {PROFILE_JSON}")

    json_output = []
    csv_rows = []

    ke_counter = {}

    for idx, profil in enumerate(profiles):
        jenis = profil["jenis_profile"]
        nama = profil["nama"]
        usia = profil["usia"]
        gender = profil["gender"]

        ke_counter.setdefault(jenis, 0)
        ke_counter[jenis] += 1
        ke = ke_counter[jenis]

        print(f"\n[{idx+1}/{len(profiles)}] {jenis} — {nama} (ke-{ke})")

        state = build_base_state(profil)

        for dtype in DIALOG_TYPES:
            print(f"  Generating {dtype}...", end=" ", flush=True)
            t_start = time.perf_counter()
            api_response = generate_dialog(dtype, state)
            t_end = time.perf_counter()
            waktu_detik = round(t_end - t_start, 3)

            # ── JSON entry: API response + metadata ──────────────────────────
            entry = {
                "jenis_profile": jenis,
                "nama": nama,
                "usia": usia,
                "gender": gender,
                "ke": ke,
            }
            entry.update(api_response)
            json_output.append(entry)

            # ── CSV: extract fields ──────────────────────────────────────────
            teks = api_response.get("dialog_text", "") or api_response.get("reaksi_npc", "")
            pilihan_jawaban = api_response.get("pilihan_jawaban", "")
            minuman_dipesan = api_response.get("minuman_dipesan", "")

            # jawaban_dipilih hanya untuk reaksi
            jawaban_dipilih = ""
            if dtype == "reaksi":
                jd = state.get("jawaban_dipilih", {})
                if isinstance(jd, dict):
                    jawaban_dipilih = jd.get("teks", "")
                elif isinstance(jd, str):
                    jawaban_dipilih = jd

            # Format pilihan_jawaban sebagai string untuk CSV
            if isinstance(pilihan_jawaban, list):
                pj_str = " | ".join(
                    f"[{p.get('id','')}] {p.get('teks','')} ({p.get('nada','')})"
                    for p in pilihan_jawaban
                )
            elif pilihan_jawaban is None:
                pj_str = ""
            else:
                pj_str = str(pilihan_jawaban)

            csv_rows.append({
                "jenis_profile": jenis,
                "nama_profile": nama,
                "jenis_dialog": dtype,
                "teks_dialog": teks,
                "pilihan_jawaban": pj_str,
                "minuman_dipesan": minuman_dipesan or "",
                "jawaban_dipilih": jawaban_dipilih,
                "waktu_detik": waktu_detik,
            })
            print(f"OK ({len(teks)} chars, {waktu_detik:.2f}s)")

    # ── Simpan JSON ───────────────────────────────────────────────────────────
    json_path = os.path.join(OUTPUT_DIR, "dialogue_testing.json")
    with open(json_path, "w", encoding="utf-8") as f:
        json.dump(json_output, f, ensure_ascii=False, indent=2)
    print(f"\n✅ JSON tersimpan: {json_path} ({len(json_output)} entries)")

    # ── Simpan CSV ────────────────────────────────────────────────────────────
    csv_path = os.path.join(OUTPUT_DIR, "dialogue_testing.csv")
    fieldnames = [
        "jenis_profile", "nama_profile", "jenis_dialog", "teks_dialog",
        "pilihan_jawaban", "minuman_dipesan", "jawaban_dipilih", "waktu_detik",
    ]
    with open(csv_path, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(csv_rows)
    print(f"✅ CSV tersimpan: {csv_path} ({len(csv_rows)} baris)")


if __name__ == "__main__":
    main()