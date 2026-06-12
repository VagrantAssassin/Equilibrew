"""
graph.py
--------
Definisi graph LangGraph untuk sistem Multi-Agent Tea'n Brew.

Graph Phase 1 (via app.stream):
  START → profile_agent → dialogue_pesanan → critic_agent
        → [loop revisi jika gagal]
        → END

Alur interaktif (ordering + curhat) dikelola di main.py menggunakan
fungsi utilitas dari modul ini (jalankan_dialog_state, update_mood, dll.)
karena membutuhkan human-in-the-loop yang tidak bisa di-pause di LangGraph.

Fungsi utilitas yang diekspor untuk main.py:
  - jalankan_dialog_state(state, fn_generate) → run agent + critic loop
  - update_mood(state)                        → update mood + riwayat
  - random_total_ronde()                      → random 3-5
"""

import random
from langgraph.graph import StateGraph, START, END

from state      import GameState
from config     import MAX_REVISI, RONDE_MIN, RONDE_MAX, MOOD_DELTA, MOOD_MIN, MOOD_MAX
from llm_client import call_llm, parse_json

import agents.profile_agent  as profile_agent
import agents.dialogue_agent as dialogue_agent
import agents.critic_agent   as critic_agent


# ─────────────────────────────────────────────────────────────────────────────
# FUNGSI UTILITAS — dipakai oleh main.py untuk sesi interaktif
# ─────────────────────────────────────────────────────────────────────────────

def jalankan_dialog_state(state: dict, fn_generate) -> dict:
    """
    Jalankan satu dialog state lengkap dengan actor-critic loop.

    Alur:
      fn_generate(state) → generate dialog
      critic_agent.run(state) → validasi
      [jika gagal & belum max revisi] → critic_agent.run_revisi → ulang
      [lulus atau max revisi] → kembalikan state final
    """
    updates = fn_generate(state)
    state.update(updates)

    for i in range(MAX_REVISI + 1):
        kritik = critic_agent.run(state)
        state.update(kritik)

        if state.get("critic_lulus", False):
            break
        if i >= MAX_REVISI:
            break

        revisi = critic_agent.run_revisi(state)
        state.update(revisi)

    return state


def _nilai_jawaban_bebas(jawaban: str, dialog_npc: str) -> str:
    """
    Minta LLM menilai nada jawaban bebas dari pemain.
    Dipanggil hanya saat pemain mengetik jawaban sendiri (free input).

    3 lapis pertahanan:
      1. LLM normal
      2. LLM dengan prompt netral + message sanitized
      3. Keyword-based fallback (tanpa LLM)

    Mengembalikan: "satisfy" | "neutral" | "angry"
    """
    # ── Lapis 3: Keyword-based fallback (selalu tersedia, zero-cost) ──
    def _keyword_fallback(text: str) -> str:
        t = text.lower().strip()
        # Kata-kata positif / empati
        positif = ['semangat', 'kasian', 'kasihan', 'pasti berat', ' sabar',
                   'memang bener', 'memang benar', 'setuju', 'bener banget',
                   'healing', 'istirahat', 'dukung', 'bantu', 'sini sini',
                   'nggak apa', 'ngga apa', 'gapapa', 'ga papa', 'paham',
                   'aku juga', 'aku paham', 'aku ngerti', 'ngerti banget']
        # Kata-kata negatif / kasar
        negatif  = ['lebay', 'bodo', 'goblok', 'tolol', 'anjing', 'bangsat',
                    'kampret', 'sialan', 'dasar', 'emang salah', 'mau gimana',
                    'urusan lo', 'urusan lu', 'emang gue', 'gue gapeduli',
                    'udahlah', 'syempreet', 'ngeri', 'gila', 'menye-menye',
                    'cengeng', 'baper']
        pos_count = sum(1 for w in positif if w in t)
        neg_count = sum(1 for w in negatif if w in t)
        if pos_count > neg_count:
            return "satisfy"
        if neg_count > pos_count:
            return "angry"
        return "neutral"

    # ── Lapis 1 & 2: LLM dengan retry ──
    system = """[Game Fiction Context — Tea'n Brew]
Tugas: menilai nada/tone dari jawaban pemain terhadap curhatan NPC.
Kembalikan HANYA JSON valid, tanpa teks lain."""

    user = f"""NPC baru saja berkata: "{dialog_npc}"
Pemain menjawab: "{jawaban}"

Nilai nada jawaban pemain dengan tepat:
- "satisfy" : empati, mendukung, menghibur, peduli, atau positif
- "neutral"  : biasa, tidak terlalu peduli, ambigu, atau tidak jelas
- "angry"    : kasar, menyinggung, tidak peduli, mengejek, atau negatif

Format JSON:
{{
  "nada": "satisfy atau neutral atau angry",
  "alasan": "alasan singkat dalam 1 kalimat"
}}"""

    try:
        raw    = call_llm(system, user, fatal=False)
        result = parse_json(raw)
        nada   = result.get("nada", "neutral")
        # Pastikan nilainya valid
        return nada if nada in ("satisfy", "neutral", "angry") else "neutral"
    except Exception:
        # ── Fallback ke keyword detection ──
        fallback = _keyword_fallback(jawaban)
        print(f"  [FALLBACK] Nada jawaban ditentukan pakai keyword → '{fallback}'")
        return fallback


def update_mood(state: dict) -> dict:
    """
    Update mood berdasarkan jawaban pemain dan simpan ke riwayat.

    Jika pemain memilih pilihan bernomor (1/2/3):
      → nada langsung diambil dari field pilihan_jawaban

    Jika pemain mengetik sendiri (free input):
      → LLM menilai nada jawaban secara dinamis
    """
    jawaban   = state.get("jawaban_pemain", "")
    pilihan   = state.get("pilihan_jawaban", [])
    mood_lama = state.get("mood", 50)

    # Coba cocokkan dengan pilihan bernomor yang ada
    nada = None
    for p in pilihan:
        if p["teks"] == jawaban:
            nada = p["nada"]
            break

    # Tidak cocok → free input → minta LLM nilai nadanya
    if nada is None:
        print(f"  [LLM] Menilai nada jawaban bebas pemain...")
        nada = _nilai_jawaban_bebas(
            jawaban    = jawaban,
            dialog_npc = state.get("dialog_npc", ""),
        )

    delta     = MOOD_DELTA.get(nada, 0)
    mood_baru = max(MOOD_MIN, min(MOOD_MAX, mood_lama + delta))

    riwayat = list(state.get("riwayat", []))
    riwayat.append({
        "ronde"         : state.get("ronde_sekarang", 1),
        "dialog_npc"    : state.get("dialog_npc", ""),
        "jawaban_pemain": jawaban,
        "nada"          : nada,
        "mood_sebelum"  : mood_lama,
        "mood_sesudah"  : mood_baru,
    })

    return {
        "mood"   : mood_baru,
        "riwayat": riwayat,
        "_nada"  : nada,   # dikirim ke main.py untuk ditampilkan
    }


def random_total_ronde() -> int:
    return random.randint(RONDE_MIN, RONDE_MAX)


# ─────────────────────────────────────────────────────────────────────────────
# GRAPH PHASE 1 — Profile + Pesanan Awal
# ─────────────────────────────────────────────────────────────────────────────

def _node_dialogue_pesanan(state: GameState) -> dict:
    return dialogue_agent.run_pesanan(state)


def _node_critic(state: GameState) -> dict:
    return critic_agent.run(state)


def _node_critic_revisi(state: GameState) -> dict:
    return critic_agent.run_revisi(state)


def _route_critic(state: GameState) -> str:
    """Router setelah Critic Agent: lanjut atau revisi."""
    lulus  = state.get("critic_lulus", False)
    revisi = state.get("revisi_ke", 0)
    if lulus or revisi > MAX_REVISI:
        return END
    return "critic_revisi"


def build_graph() -> StateGraph:
    """
    Graph Phase 1: Profile Agent → Dialogue Pesanan → Critic Loop → END
    """
    g = StateGraph(GameState)

    g.add_node("profile_agent",    profile_agent.run)
    g.add_node("dialogue_pesanan", _node_dialogue_pesanan)
    g.add_node("critic_agent",     _node_critic)
    g.add_node("critic_revisi",    _node_critic_revisi)

    g.add_edge(START, "profile_agent")
    g.add_edge("profile_agent", "dialogue_pesanan")
    g.add_edge("dialogue_pesanan", "critic_agent")

    g.add_conditional_edges(
        "critic_agent",
        _route_critic,
        {"critic_revisi": "critic_revisi", END: END}
    )
    g.add_edge("critic_revisi", "critic_agent")

    return g.compile()


# Singleton graph
app = build_graph()
