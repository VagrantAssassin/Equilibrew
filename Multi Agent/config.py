"""
config.py
---------
Semua konfigurasi sistem dibaca dari file .env menggunakan python-dotenv.
Tidak ada credential yang di-hardcode di sini.

Cara setup:
  1. cp .env.example .env
  2. Isi variabel di .env sesuai provider yang dipakai
  3. Jalankan: python main.py
"""

import os
import sys
from dotenv import load_dotenv

load_dotenv()

# ── API ───────────────────────────────────────────────────────────────────────
API_URL = os.getenv("API_URL", "")
API_KEY = os.getenv("API_KEY", "")
MODEL   = os.getenv("MODEL",   "")

if not API_URL:
    print("\n[ERROR] API_URL tidak ditemukan di file .env!")
    print("  Contoh: API_URL=http://localhost:20128/v1")
    sys.exit(1)

if not API_KEY:
    print("\n[ERROR] API_KEY tidak ditemukan di file .env!")
    print("  Contoh: API_KEY=sk-xxxxxxxxxxxx")
    sys.exit(1)

if not MODEL:
    print("\n[ERROR] MODEL tidak ditemukan di file .env!")
    print("  Contoh: MODEL=gpt-5.4")
    sys.exit(1)

# ── LLM ───────────────────────────────────────────────────────────────────────
MAX_TOKENS = int(os.getenv("MAX_TOKENS", "1500"))

# ── ACTOR-CRITIC ──────────────────────────────────────────────────────────────
MAX_REVISI       = int(os.getenv("MAX_REVISI", "2"))
CRITIC_THRESHOLD = int(os.getenv("CRITIC_THRESHOLD", "70"))

# ── MOOD ──────────────────────────────────────────────────────────────────────
MOOD_AWAL  = int(os.getenv("MOOD_AWAL", "50"))
MOOD_MIN   = 0
MOOD_MAX   = 100
MOOD_DELTA = {"satisfy": +10, "neutral": 0, "angry": -10}

# ── SESI CURHAT ───────────────────────────────────────────────────────────────
RONDE_MIN = int(os.getenv("RONDE_MIN", "3"))
RONDE_MAX = int(os.getenv("RONDE_MAX", "5"))

# ── DATA GAME ─────────────────────────────────────────────────────────────────
GAYA_BAHASA = {
    "remaja"    : "santai, gaul, pakai kata seperti 'kak', 'bro', 'sih', 'dong', 'tuh'",
    "dewasa"    : "lugas, sopan tapi natural, kadang sedikit formal",
    "orang tua" : "formal, bijak, sering pakai 'nak', kalimat panjang dan reflektif",
}

# Minuman aktif (3 pilihan sementara, nanti input dari Unity)
MENU_MINUMAN = [
    "Black Tea",
    "Mint Tea",
    "Green Tea",
]
