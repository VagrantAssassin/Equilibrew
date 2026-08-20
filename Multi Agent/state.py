"""
state.py
--------
GameState — satu-satunya objek state yang mengalir di seluruh graph LangGraph.

7 Dialog State NPC sesuai skenario game Tea'n Brew (Section 3.1.1):
  1. pesanan       → NPC datang dan memesan minuman
  2. pesanan_salah → NPC bereaksi saat pemain membuat pesanan yang salah
  3. marah         → NPC marah dan pergi setelah mencapai batas fail
  4. berhasil      → NPC puas karena pesanan benar, transisi ke curhat
  5. curhat        → Sesi curhat multi-ronde (3-5 ronde)
    6. reaksi        → NPC merespons jawaban pemain di sesi curhat
    7. closing       → NPC menutup sesi curhat
"""

from typing import TypedDict


class OceanScore(TypedDict):
    openness:          int   # 0–100
    conscientiousness: int
    extraversion:      int
    agreeableness:     int
    neuroticism:       int


class PilihanJawaban(TypedDict):
    id:   int
    teks: str
    nada: str   # "satisfy" | "neutral" | "angry"


class RiwayatRonde(TypedDict):
    ronde:          int
    dialog_npc:     str
    jawaban_pemain: str
    nada:           str
    mood_sebelum:   int
    mood_sesudah:   int


class GameState(TypedDict, total=False):
    # ── INPUT dari game (Unity) ───────────────────────────────────────────────
    usia:   str   # "remaja" | "dewasa" | "orang tua"
    gender: str   # "pria" | "wanita"

    # ── OUTPUT Profile Agent ──────────────────────────────────────────────────
    nama:             str
    background:       str
    masalah_hari_ini: str
    ocean:            OceanScore
    max_fails:        int   # toleransi salah pesanan NPC (1–3, sesuai minMaxFails game)

    # ── State dialog aktif ────────────────────────────────────────────────────
    # Nilai: "pesanan" | "pesanan_salah" | "marah" | "berhasil" |
    #        "curhat" | "reaksi" | "closing"
    dialog_state: str

    # ── Output Dialogue Agent per state ───────────────────────────────────────
    minuman_dipesan:      str    # state pesanan
    dialog_pesanan:       str    # state pesanan
    dialog_pesanan_salah: str    # state pesanan_salah
    dialog_marah:         str    # state marah
    dialog_berhasil:      str    # state berhasil
    dialog_npc:           str    # state curhat — dialog per ronde
    pilihan_jawaban:      list   # state curhat — 3 pilihan untuk pemain
    reaksi_npc:           str    # state curhat — reaksi setelah pemain menjawab
    dialog_closing:       str    # state curhat — penutup setelah ronde terakhir

    # ── Kontrol ordering ─────────────────────────────────────────────────────
    fail_count: int   # jumlah salah pesanan saat ini

    # ── Critic Agent ─────────────────────────────────────────────────────────
    critic_lulus:        bool
    critic_skor:         float
    critic_skor_dimensi: dict
    critic_catatan:      list
    critic_saran:        str
    konteks_critic:      str
    revisi_ke:           int

    # ── Sesi curhat ───────────────────────────────────────────────────────────
    ronde_sekarang: int
    total_ronde:    int
    jawaban_pemain: str

    # ── Memori ────────────────────────────────────────────────────────────────
    mood:    int    # 0–100, short-term memory sesi ini
    riwayat: list   # list[RiwayatRonde] — riwayat percakapan sesi curhat
