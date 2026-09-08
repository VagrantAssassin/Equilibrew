"""
experiment_dialogue_state.py
-----------------------------
Eksperimen Dialogue Agent — Batch semua 7 dialog state.

Menghasilkan: 6 profil (remaja/dewasa/orang tua × pria/wanita)
              × 7 state × 3 output = 126 dialog.

7 dialog state (sesuai state.py):
  1. pesanan       → run_pesanan()
  2. pesanan_salah → run_pesanan_salah()
  3. marah         → run_marah()
  4. berhasil      → run_berhasil()
  5. curhat        → run_curhat()
  6. reaksi        → run_reaksi()
  7. closing       → run_closing()

Output mengikuti STRUKTUR JSON YANG SAMA dengan response yang dikirim server.py
ke client Unity:
  - 6 state dialog (pesanan/pesanan_salah/marah/berhasil/curhat/closing)
    → bentuk DialogResponse (dialog_text, dialog_state, critic_skor, dll.)
  - state reaksi → bentuk EvaluateResponse (reaksi_npc, nada, mood, riwayat_entry)

Setiap state dijalankan lewat jalankan_dialog_state() (Actor-Critic loop yang
sama dengan server), BUKAN memanggil agent secara mentah, sehingga field
critic_skor/critic_lulus/critic_log juga terisi nyata.

Pengujian FOKUS pada format: apakah struktur output setiap state sudah sesuai
atau belum (key lengkap, tipe benar, tidak kosong).

Cara pakai:
  cd "d:/File Unity/Equilibrew/Multi Agent"
  python experiment_dialogue_state.py

Output:
  exp_dialogue_state/results.json      → hasil loop (struktur DialogResponse/EvaluateResponse)
  exp_dialogue_state/validation.json   → hasil cek format (per output)
  exp_dialogue_state/validation.csv    → hasil cek format (CSV)
"""

import json
import os
import random
import sys
import time
import uuid

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from config import MOOD_AWAL, MENU_MINUMAN
from graph import jalankan_dialog_state, update_mood
import agents.profile_agent as profile_agent
import agents.dialogue_agent as dialogue_agent


# ── 6 profil (3 usia × 2 gender) ──────────────────────────────────────────────
PROFILES = [
    {"usia": "remaja", "gender": "pria"},
    {"usia": "remaja", "gender": "wanita"},
    {"usia": "dewasa", "gender": "pria"},
    {"usia": "dewasa", "gender": "wanita"},
    {"usia": "orang tua", "gender": "pria"},
    {"usia": "orang tua", "gender": "wanita"},
]

JUMLAH_PER = 3   # jumlah output per state per profil

OUT_DIR = "exp_dialogue_state"
os.makedirs(OUT_DIR, exist_ok=True)

# ── Struktur output API (sesuai server.py) ───────────────────────────────────
# Eksperimen ini mengikuti response_model yang SAMA dengan yang diterima client
# Unity, sehingga hasil loop tidak "ngasal":
#   - 6 state dialog → DialogResponse
#   - reaksi          → EvaluateResponse
# Field Optional dibiarkan None persis seperti endpoint yang tidak mengisinya.

DIALOG_RESPONSE_FIELDS = [
    "session_id", "dialog_state", "dialog_text", "minuman_dipesan",
    "pilihan_jawaban", "critic_skor", "critic_lulus", "critic_log",
    "total_ronde", "ronde_sekarang",
]

EVALUATE_RESPONSE_FIELDS = [
    "session_id", "nada", "mood_sebelum", "mood_sesudah", "reaksi_npc",
    "riwayat_entry", "total_ronde", "ronde_sekarang",
    "critic_skor", "critic_lulus", "critic_log",
]

# Mapping state → fungsi dialogue agent
STATE_FNS = {
    "pesanan":       dialogue_agent.run_pesanan,
    "pesanan_salah": dialogue_agent.run_pesanan_salah,
    "marah":         dialogue_agent.run_marah,
    "berhasil":      dialogue_agent.run_berhasil,
    "curhat":        dialogue_agent.run_curhat,
    "reaksi":        dialogue_agent.run_reaksi,
    "closing":       dialogue_agent.run_closing,
}

STATE_ORDER = [
    "pesanan", "pesanan_salah", "marah", "berhasil",
    "curhat", "reaksi", "closing",
]


def fresh_profile(usia: str, gender: str) -> dict:
    """Generate satu profil NPC baru (state awal untuk semua state dialog)."""
    state = {"usia": usia, "gender": gender, "mood": MOOD_AWAL}
    updates = profile_agent.run(state)
    state.update(updates)

    # Lengkapi field yang dibutuhkan state-state dialog
    state["session_id"] = str(uuid.uuid4())[:8]
    state["minuman_dipesan"] = random.choice(MENU_MINUMAN)
    state["ronde_sekarang"] = 1
    state["total_ronde"] = 3
    state["fail_count"] = 1
    state["riwayat"] = []
    return state


def build_reaksi_context(state: dict) -> None:
    """Simulasikan jawaban pemain: pilih jawaban bernomor pertama.

    Mood & riwayat dihitung oleh update_mood() di run_state (bukan di-hardcode),
    supaya konsisten dengan endpoint /evaluate_answer dan tidak "ngasal".
    """
    pilihan = state.get("pilihan_jawaban", [])
    if pilihan and isinstance(pilihan[0], dict):
        state["jawaban_pemain"] = pilihan[0].get("teks", "iya kak")
    else:
        state["jawaban_pemain"] = "iya kak"
    state["mood"] = state.get("mood", MOOD_AWAL)


def build_closing_context(state: dict) -> None:
    """Isi context tambahan yang dibutuhkan run_closing."""
    state["mood"] = state.get("mood", MOOD_AWAL)
    if not state.get("riwayat"):
        state["riwayat"] = []
    state.setdefault("tema", "masalah pribadi")


def _wrap_dialog_response(state: dict) -> dict:
    """Bungkus state menjadi object berstruktur DialogResponse (sesuai server.py)."""
    dialog_state = state.get("dialog_state", "")
    dialog_text = {
        "pesanan":       state.get("dialog_pesanan", ""),
        "pesanan_salah": state.get("dialog_pesanan_salah", ""),
        "marah":         state.get("dialog_marah", ""),
        "berhasil":      state.get("dialog_berhasil", ""),
        "curhat":        state.get("dialog_npc", ""),
        "closing":       state.get("dialog_closing", ""),
    }.get(dialog_state, "")

    return {
        "session_id": state.get("session_id", ""),
        "dialog_state": dialog_state,
        "dialog_text": dialog_text,
        "minuman_dipesan": state.get("minuman_dipesan") if dialog_state == "pesanan" else None,
        "pilihan_jawaban": state.get("pilihan_jawaban") if dialog_state == "curhat" else None,
        "critic_skor": state.get("critic_skor"),
        "critic_lulus": state.get("critic_lulus"),
        "critic_log": state.get("critic_log"),
        "total_ronde": state.get("total_ronde"),
        "ronde_sekarang": state.get("ronde_sekarang") if dialog_state in ("curhat", "closing") else None,
    }


def _wrap_evaluate_response(state: dict) -> dict:
    """Bungkus state menjadi object berstruktur EvaluateResponse (sesuai server.py)."""
    riwayat = state.get("riwayat", [])
    return {
        "session_id": state.get("session_id", ""),
        "nada": state.get("_nada", "neutral"),
        "mood_sebelum": state.get("_mood_sebelum", MOOD_AWAL),
        "mood_sesudah": state.get("mood", MOOD_AWAL),
        "reaksi_npc": state.get("reaksi_npc", ""),
        "riwayat_entry": riwayat[-1] if riwayat else {},
        "total_ronde": state.get("total_ronde"),
        "ronde_sekarang": state.get("ronde_sekarang"),
        "critic_skor": state.get("critic_skor"),
        "critic_lulus": state.get("critic_lulus"),
        "critic_log": state.get("critic_log"),
    }


def run_state(state: dict, state_name: str) -> dict:
    """Jalankan satu dialog state lewat Actor-Critic loop, lalu bungkus ke
    struktur JSON API (DialogResponse/EvaluateResponse)."""
    fn = STATE_FNS[state_name]

    if state_name == "reaksi":
        mood_sebelum = state.get("mood", MOOD_AWAL)
        state["_mood_sebelum"] = mood_sebelum

        # 1) Update mood + simpan riwayat (sama seperti /evaluate_answer)
        mood_update = update_mood(state)
        state.update(mood_update)

        # 2) Generate reaksi NPC lewat Actor-Critic loop
        state["dialog_state"] = "reaksi"
        state["konteks_critic"] = "reaksi"
        state["revisi_ke"] = 0
        state = jalankan_dialog_state(state, fn)

        # 3) Patch riwayat terakhir dengan reaksi_npc (sama seperti endpoint)
        riwayat = state.get("riwayat", [])
        reaksi_text = state.get("reaksi_npc", "")
        if riwayat and reaksi_text:
            riwayat[-1]["reaksi_npc"] = reaksi_text
            state["riwayat"] = riwayat

        return _wrap_evaluate_response(state)

    # 6 state dialog lainnya
    state["dialog_state"] = state_name
    state["konteks_critic"] = state_name
    state["revisi_ke"] = 0

    state = jalankan_dialog_state(state, fn)
    return _wrap_dialog_response(state)


def validate(state_name: str, output: dict) -> list:
    """Periksa struktur output API untuk satu state. Kembalikan list isu (kosong = valid)."""
    issues = []

    if state_name == "reaksi":
        for key in EVALUATE_RESPONSE_FIELDS:
            if key not in output:
                issues.append(f"missing key '{key}'")
                continue
            val = output[key]
            if key == "nada":
                if val not in ("satisfy", "neutral", "angry"):
                    issues.append(f"'nada' tidak valid: {val!r}")
            elif key in ("mood_sebelum", "mood_sesudah", "total_ronde", "ronde_sekarang"):
                if not isinstance(val, int):
                    issues.append(f"'{key}' bukan int: {type(val).__name__}")
            elif key == "riwayat_entry":
                if not isinstance(val, dict):
                    issues.append("'riwayat_entry' bukan object")
            elif key == "critic_log":
                if val is not None and not isinstance(val, list):
                    issues.append("'critic_log' bukan list/null")
            elif key == "reaksi_npc":
                if not isinstance(val, str) or not val.strip():
                    issues.append("'reaksi_npc' kosong atau bukan string")
        return issues

    # 6 state dialog → DialogResponse
    for key in DIALOG_RESPONSE_FIELDS:
        if key not in output:
            issues.append(f"missing key '{key}'")
            continue
        val = output[key]

        if key == "dialog_text":
            if not isinstance(val, str) or not val.strip():
                issues.append("'dialog_text' kosong atau bukan string")
        elif key == "dialog_state":
            if not isinstance(val, str) or not val.strip():
                issues.append("'dialog_state' kosong atau bukan string")
        elif key == "pilihan_jawaban" and state_name == "curhat":
            if not isinstance(val, list):
                issues.append("'pilihan_jawaban' bukan list")
            elif len(val) != 3:
                issues.append(f"'pilihan_jawaban' harus 3 item, dapat {len(val)}")
            else:
                for i, p in enumerate(val):
                    if not isinstance(p, dict):
                        issues.append(f"pilihan_jawaban[{i}] bukan object")
                        continue
                    if not isinstance(p.get("id"), int):
                        issues.append(f"pilihan_jawaban[{i}].id bukan int")
                    if not isinstance(p.get("teks"), str) or not p.get("teks", "").strip():
                        issues.append(f"pilihan_jawaban[{i}].teks kosong")
                    if p.get("nada") not in ("satisfy", "neutral", "angry"):
                        issues.append(f"pilihan_jawaban[{i}].nada tidak valid: {p.get('nada')!r}")
        elif key == "critic_log":
            if val is not None and not isinstance(val, list):
                issues.append("'critic_log' bukan list/null")
        elif key == "critic_skor":
            if val is not None and not isinstance(val, (int, float)):
                issues.append(f"'critic_skor' bukan angka/null: {type(val).__name__}")
        elif key in ("total_ronde", "ronde_sekarang"):
            if val is not None and not isinstance(val, int):
                issues.append(f"'{key}' bukan int/null: {type(val).__name__}")
        elif key == "minuman_dipesan" and state_name == "pesanan":
            if not isinstance(val, str) or not val.strip():
                issues.append("'minuman_dipesan' kosong atau bukan string")
    return issues


def main():
    print("=" * 62)
    print("DIALOGUE AGENT EXPERIMENT — 7 STATES")
    print(f"Profil : {len(PROFILES)} kategori (remaja/dewasa/orang tua × pria/wanita)")
    print(f"States : {len(STATE_ORDER)} ({', '.join(STATE_ORDER)})")
    print(f"Output : {JUMLAH_PER} per state per profil")
    print(f"Total  : {len(PROFILES) * len(STATE_ORDER) * JUMLAH_PER} dialog")
    print("=" * 62)

    results = {}        # {profil: {state: [out1, out2, out3]}}
    validation = []     # [{profil, state, rep, valid, issues}]

    start_time = time.time()

    for prof in PROFILES:
        label = f"{prof['usia']}_{prof['gender']}"
        results[label] = {}
        print(f"\n{'─' * 52}")
        print(f"PROFIL: {label}")
        for rep in range(JUMLAH_PER):
            # Satu NPC baru utk tiap rep (biar tiap output beda NPC)
            state = fresh_profile(prof["usia"], prof["gender"])

            for state_name in STATE_ORDER:
                if state_name == "reaksi":
                    build_reaksi_context(state)
                elif state_name == "closing":
                    build_closing_context(state)

                try:
                    output = run_state(state, state_name)
                    results[label].setdefault(state_name, []).append(output)
                    issues = validate(state_name, output)
                    valid = len(issues) == 0
                    validation.append({
                        "profil": label,
                        "state": state_name,
                        "rep": rep + 1,
                        "valid": valid,
                        "issues": issues,
                    })
                    mark = "✓" if valid else "✗"
                    print(f"  [{rep+1}] {state_name:<14} {mark} {('; '.join(issues)) if issues else ''}")
                except Exception as e:
                    results[label].setdefault(state_name, []).append({"error": str(e)})
                    validation.append({
                        "profil": label,
                        "state": state_name,
                        "rep": rep + 1,
                        "valid": False,
                        "issues": [f"exception: {e}"],
                    })
                    print(f"  [{rep+1}] {state_name:<14} ✗ exception: {e}")

                time.sleep(0.2)

    elapsed = time.time() - start_time

    # ── Simpan hasil JSON ─────────────────────────────────────────────────────
    results_path = os.path.join(OUT_DIR, "results.json")
    with open(results_path, "w", encoding="utf-8") as f:
        json.dump(results, f, indent=2, ensure_ascii=False)

    validation_path = os.path.join(OUT_DIR, "validation.json")
    with open(validation_path, "w", encoding="utf-8") as f:
        json.dump(validation, f, indent=2, ensure_ascii=False)

    # ── Simpan validation CSV ─────────────────────────────────────────────────
    csv_path = os.path.join(OUT_DIR, "validation.csv")
    with open(csv_path, "w", encoding="utf-8") as f:
        f.write("profil,state,rep,valid,issues\n")
        for v in validation:
            issues_str = ("; ".join(v["issues"])).replace('"', '""').replace(",", ";")
            f.write(f'{v["profil"]},{v["state"]},{v["rep"]},{v["valid"]},"{issues_str}"\n')

    # ── Ringkasan ─────────────────────────────────────────────────────────────
    total = len(validation)
    valid_count = sum(1 for v in validation if v["valid"])
    invalid_count = total - valid_count

    print("\n" + "=" * 62)
    print("RINGKASAN")
    print(f"  Total output : {total}")
    print(f"  Valid        : {valid_count}")
    print(f"  Invalid      : {invalid_count}")
    print(f"  Waktu        : {elapsed:.0f} detik")
    print(f"  Output dir   : {os.path.abspath(OUT_DIR)}")
    print("=" * 62)

    if invalid_count > 0:
        print("\nDETAIL YANG INVALID:")
        for v in validation:
            if not v["valid"]:
                print(f"  - [{v['profil']}/{v['state']}/rep{v['rep']}] {v['issues']}")


if __name__ == "__main__":
    main()