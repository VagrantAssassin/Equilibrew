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
MODEL   = os.getenv("MODEL", "")
PROFILE_MODEL  = os.getenv("PROFILE_MODEL", "")
DIALOGUE_MODEL = os.getenv("DIALOGUE_MODEL", "")
CRITIC_MODEL   = os.getenv("CRITIC_MODEL", "")  

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

if not PROFILE_MODEL:
    print("\n[ERROR] PROFILE_MODEL tidak ditemukan di file .env!")
    print("  Contoh: PROFILE_MODEL=gpt-5.6-luna")
    sys.exit(1)

if not DIALOGUE_MODEL:
    print("\n[ERROR] DIALOGUE_MODEL tidak ditemukan di file .env!")
    print("  Contoh: DIALOGUE_MODEL=gpt-5.6-luna")
    sys.exit(1)

if not CRITIC_MODEL:
    print("\n[ERROR] CRITIC_MODEL tidak ditemukan di file .env!")
    print("  Contoh: CRITIC_MODEL=gpt-5.6-sol")
    sys.exit(1)

# ── LLM ───────────────────────────────────────────────────────────────────────
MAX_TOKENS = int(os.getenv("MAX_TOKENS", "1500"))

# ── HYPERPARAMETER PER AGENT ──────────────────────────────────────────────────
# Profile Agent: kreativitas tinggi untuk variasi profil karakter
PROFILE_TEMPERATURE        = float(os.getenv("PROFILE_TEMPERATURE", "0.8"))
PROFILE_FREQUENCY_PENALTY  = float(os.getenv("PROFILE_FREQUENCY_PENALTY", "0.3"))
PROFILE_PRESENCE_PENALTY   = float(os.getenv("PROFILE_PRESENCE_PENALTY", "0.2"))

# Dialogue Agent: seimbang antara kreatif dan konsisten untuk dialog natural
DIALOGUE_TEMPERATURE        = float(os.getenv("DIALOGUE_TEMPERATURE", "0.5"))
DIALOGUE_FREQUENCY_PENALTY  = float(os.getenv("DIALOGUE_FREQUENCY_PENALTY", "0.4"))
DIALOGUE_PRESENCE_PENALTY   = float(os.getenv("DIALOGUE_PRESENCE_PENALTY", "0.0"))

# Critic Agent: presisi tinggi untuk evaluasi yang konsisten
CRITIC_TEMPERATURE        = float(os.getenv("CRITIC_TEMPERATURE", "0.2"))
CRITIC_FREQUENCY_PENALTY  = float(os.getenv("CRITIC_FREQUENCY_PENALTY", "0.1"))
CRITIC_PRESENCE_PENALTY   = float(os.getenv("CRITIC_PRESENCE_PENALTY", "0.0"))

# ── ACTOR-CRITIC ──────────────────────────────────────────────────────────────
MAX_REVISI       = int(os.getenv("MAX_REVISI", "2"))
CRITIC_THRESHOLD = float(os.getenv("CRITIC_THRESHOLD", "4.0"))

# ── MOOD ──────────────────────────────────────────────────────────────────────
MOOD_AWAL  = int(os.getenv("MOOD_AWAL", "50"))
MOOD_MIN   = 0
MOOD_MAX   = 100
# Perubahan mood mengikuti mekanik asli game.
MOOD_DELTA = {"satisfy": +10, "neutral": 0, "angry": -10}

# ── SESI CURHAT ───────────────────────────────────────────────────────────────
RONDE_MIN = int(os.getenv("RONDE_MIN", "3"))
RONDE_MAX = int(os.getenv("RONDE_MAX", "5"))

# ── DATA GAME ─────────────────────────────────────────────────────────────────
GAYA_BAHASA = {
    "anak-anak" : "polos, lucu, pakai kata sederhana, kadang salah ucap, suka bilang 'dong', 'nih'",
    "remaja"    : "santai, gaul, pakai kata seperti 'kak', 'sih', 'dong', 'tuh', 'kan'. Panggil pemain 'kak'.",
    "dewasa"    : "lugas, sopan tapi natural, kadang sedikit formal",
    "orang tua" : "formal, bijak, sering pakai 'nak', kalimat panjang dan reflektif",
}

# Minuman aktif (3 pilihan sementara, nanti input dari Unity)
MENU_MINUMAN = [
    "Black Tea",
    "Mint Tea",
    "Green Tea",
    "Matcha Latte",
    "Milk Tea",
    "Mint Milk Tea",
    "Apple Tea",
    "Lavender Tea",
    "Jasmine Tea",
]
