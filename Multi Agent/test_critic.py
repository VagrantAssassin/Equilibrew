"""
test_critic.py
--------------
Test suite untuk Critic Agent — validasi G-Eval + 8 dimensi FED.

Menguji 2 skenario:
  1. Skenario GOOD — dialog yang sesuai profil OCEAN, gaya bahasa, dan emosi state.
     Ekspektasi: skor TINGGI (>= CRITIC_THRESHOLD), lulus = True.
  2. Skenario BAD  — dialog yang sengaja dibuat ngaco (tidak sesuai OCEAN, generik,
     emosi salah, gaya bahasa tidak match).
     Ekspektasi: skor RENDAH (< CRITIC_THRESHOLD), lulus = False.

Cara menjalankan:
  cd "d:\\File Unity\\Equilibrew\\Multi Agent"
  python test_critic.py
"""

import json
import sys
import os

# Pastikan folder Multi Agent ada di path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from config     import CRITIC_THRESHOLD
from agents.critic_agent import run as critic_run


# ─────────────────────────────────────────────────────────────────────────────
# SKENARIO 1: GOOD — Dialog yang sesuai profil & konteks
# ─────────────────────────────────────────────────────────────────────────────
# Profil: Raka, remaja, pria, Extrovert tinggi (E=78), Neurotic tinggi (N=72),
#         Agreeableness rendah (A=35) → blak-blakan, antusias, emosional
# State : "pesanan" → emosi santai, sopan, sedikit gelisah
# Dialog: Sesuai gaya remaja gaul, antusias, menyebut masalah spesifik
# ─────────────────────────────────────────────────────────────────────────────
GOOD_STATE = {
    "nama"             : "Raka",
    "usia"             : "remaja",
    "gender"           : "pria",
    "ocean": {
        "openness"         : 85,
        "conscientiousness": 80,
        "extraversion"     : 20,
        "agreeableness"    : 25,
        "neuroticism"      : 35,
    },
    "konteks_critic"   : "pesanan",
    "minuman_dipesan"  : "Green Tea",
    "dialog_pesanan"   : "Saya ingin memesan green tea, cepat ya saya buru buru.",
}


# ─────────────────────────────────────────────────────────────────────────────
# SKENARIO 2: BAD — Dialog yang sengaja dibuat ngaco
# ─────────────────────────────────────────────────────────────────────────────
# Profil: SAMA PERSIS dengan skenario GOOD (E=78, N=72, A=35 → harus antusias,
#         emosional, blak-blakan)
# State : "pesanan" → harusnya santai, sopan, sedikit gelisah
# Dialog NGACO:
#   - Gaya bahasa FORMAL sekali (padahal usia remaja → harusnya gaul)
#   - SANGAT pendek (padahal E=78 → harusnya panjang & antusias)
#   - Tidak menyebut masalah apa pun (padahal N=72 → harus emosional/gelisah)
#   - Emosi MARAH (padahal state pesanan → harusnya santai/sopan)
#   - Generik, tidak spesifik, tidak natural
# ─────────────────────────────────────────────────────────────────────────────
BAD_STATE = {
    "nama"             : "Raka",
    "usia"             : "remaja",
    "gender"           : "pria",
    "ocean"            : {
        "openness"         : 62,
        "conscientiousness": 55,
        "extraversion"     : 78,
        "agreeableness"    : 35,
        "neuroticism"      : 72,
    },
    "konteks_critic"   : "pesanan",
    "minuman_dipesan"  : "Green Tea",
    "dialog_pesanan"   : (
        "Eh kak, akhirnya sampai juga! Pesen Green Tea ya kak, soalnya kepala aku "
        "penuh banget tadi mulai dari deadline tugas kelompok yang dimajuin besok "
        "sampe anggota tim yang pada ngilang. Serius deh, butuh teh buat ngecabin "
        "malam nanti. Makasih ya kak!"
    ),
}


# ─────────────────────────────────────────────────────────────────────────────
# Runner
# ─────────────────────────────────────────────────────────────────────────────

def _print_separator(title: str):
    print("\n" + "=" * 70)
    print(f"  {title}")
    print("=" * 70)


def _print_result(label: str, state: dict, expect_pass: bool):
    print(f"\n--- {label} ---")
    print(f"  Nama        : {state['nama']}")
    print(f"  Usia/Gender : {state['usia']} / {state['gender']}")
    print(f"  OCEAN       : E={state['ocean']['extraversion']}, "
          f"N={state['ocean']['neuroticism']}, A={state['ocean']['agreeableness']}")
    print(f"  Konteks     : {state['konteks_critic']}")
    print(f"  Dialog      : \"{state['dialog_pesanan'][:80]}...\"")
    print()

    try:
        result = critic_run(state)
    except Exception as e:
        print(f"  [ERROR] Critic agent gagal: {e}")
        return False

    skor        = result.get("critic_skor", 0)
    lulus       = result.get("critic_lulus", False)
    skor_dim    = result.get("critic_skor_dimensi", {})
    catatan     = result.get("critic_catatan", [])
    saran       = result.get("critic_saran", "")

    print(f"  SKOR TOTAL  : {skor:.2f} / 5.0   (threshold: {CRITIC_THRESHOLD})")
    print(f"  LULUS       : {lulus}")
    print()
    print("  SKOR PER DIMENSI (FED):")
    for dim, val in skor_dim.items():
        bar = "█" * int(val) + "░" * (5 - int(val))
        print(f"    {dim:28s} {bar} {val}/5")
    print()
    print(f"  CATATAN     : {catatan}")
    print(f"  SARAN       : {saran}")
    print()

    # Validasi ekspektasi
    if expect_pass and lulus:
        print(f"  ✅ EKSPEKTASI TERPENUHI: Skor TINGGI, dialog LULUS")
        return True
    elif not expect_pass and not lulus:
        print(f"  ✅ EKSPEKTASI TERPENUHI: Skor RENDAH, dialog TIDAK LULUS")
        return True
    else:
        print(f"  ⚠️  EKSPEKTASI TIDAK SESUAI: expect_pass={expect_pass}, "
              f"lulus={lulus}, skor={skor:.2f}")
        return False


def main():
    print("\n" + "╔" + "═" * 68 + "╗")
    print("║" + "  CRITIC AGENT TEST — G-Eval + 8 Dimensi FED".center(68) + "║")
    print("║" + f"  Threshold: {CRITIC_THRESHOLD}/5.0".center(68) + "║")
    print("╚" + "═" * 68 + "╝")

    results = []

    # ── Skenario 1: GOOD ──
    _print_separator("SKENARIO 1: GOOD — Dialog Sesuai Profil")
    results.append(_print_result("GOOD", GOOD_STATE, expect_pass=True))

    # ── Skenario 2: BAD ──
    _print_separator("SKENARIO 2: BAD — Dialog Ngaco")
    results.append(_print_result("BAD", BAD_STATE, expect_pass=False))

    # ── Ringkasan ──
    _print_separator("RINGKASAN")
    passed = sum(results)
    total  = len(results)
    print(f"\n  {passed}/{total} skenario memenuhi ekspektasi.")
    if passed == total:
        print("  🎉 Semua skenario sesuai ekspektasi!")
    else:
        print("  ⚠️  Ada skenario yang tidak sesuai ekspektasi. Cek detail di atas.")
    print()

    return 0 if passed == total else 1


if __name__ == "__main__":
    sys.exit(main())
