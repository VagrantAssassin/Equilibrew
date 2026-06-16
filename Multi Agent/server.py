"""
server.py — FastAPI bridge antara Unity dan Multi-Agent system.

Unity mengirim HTTP request → server menjalankan agent → mengembalikan JSON response.

Endpoints:
  POST /generate_profile    → Profile Agent generates NPC
  POST /generate_pesanan    → Dialogue Agent: ordering dialog
  POST /generate_salah      → Dialogue Agent: wrong order reaction
  POST /generate_marah      → Dialogue Agent: angry & leaving
  POST /generate_berhasil   → Dialogue Agent: success reaction
  POST /generate_curhat     → Dialogue Agent: curhat round
  POST /evaluate_answer     → Mood update + reaction to player answer
  GET  /health              → Health check
"""

import uuid
import uvicorn
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field
from typing import Optional, List, Dict, Any

from config import MOOD_AWAL, MOOD_DELTA, MOOD_MIN, MOOD_MAX, MENU_MINUMAN
from graph import jalankan_dialog_state, update_mood, random_total_ronde
from llm_client import call_llm, parse_json

import agents.profile_agent  as profile_agent
import agents.dialogue_agent as dialogue_agent
import agents.critic_agent   as critic_agent

app = FastAPI(title="Tea'n Brew Multi-Agent API", version="1.0.0")

# ── In-memory session storage ─────────────────────────────────────────────────
# Key: session_id, Value: GameState dict
sessions: Dict[str, dict] = {}


# ── Request / Response Models ─────────────────────────────────────────────────

class ProfileRequest(BaseModel):
    usia: str = Field(..., description="Kategori usia: remaja/dewasa/orang tua")
    gender: str = Field(..., description="Gender: pria/wanita")
    menu: Optional[List[str]] = Field(None, description="Daftar menu minuman (opsional)")

class ProfileResponse(BaseModel):
    session_id: str
    nama: str
    background: str
    masalah_hari_ini: str
    ocean: dict
    max_fails: int

class DialogRequest(BaseModel):
    session_id: str

class DialogResponse(BaseModel):
    session_id: str
    dialog_state: str
    dialog_text: str
    minuman_dipesan: Optional[str] = None
    pilihan_jawaban: Optional[List[dict]] = None
    critic_skor: Optional[int] = None
    critic_lulus: Optional[bool] = None
    total_ronde: Optional[int] = None
    ronde_sekarang: Optional[int] = None

class CurhatStartRequest(BaseModel):
    session_id: str
    total_ronde: Optional[int] = None

class CurhatRoundRequest(BaseModel):
    session_id: str
    ronde: int

class EvaluateRequest(BaseModel):
    session_id: str
    jawaban_pemain: str
    chosen_nada: Optional[str] = Field(None, description="Nada dari pilihan: satisfy/neutral/angry. Kosong jika free input.")

class EvaluateResponse(BaseModel):
    session_id: str
    nada: str
    mood_sebelum: int
    mood_sesudah: int
    reaksi_npc: str
    riwayat_entry: dict
    total_ronde: Optional[int] = None
    ronde_sekarang: Optional[int] = None

class SessionSummaryResponse(BaseModel):
    session_id: str
    nama: str
    usia: str
    gender: str
    mood_awal: int
    mood_akhir: int
    riwayat: list


# ── Endpoints ─────────────────────────────────────────────────────────────────

@app.get("/health")
def health():
    return {"status": "ok", "active_sessions": len(sessions)}


@app.post("/generate_profile", response_model=ProfileResponse)
def generate_profile(req: ProfileRequest):
    """
    Generate NPC profile baru. Memanggil Profile Agent.
    Mengembalikan session_id untuk dipakai di endpoint selanjutnya.
    """
    # Update menu jika dikirim dari Unity
    if req.menu and len(req.menu) > 0:
        import config
        config.MENU_MINUMAN = req.menu

    session_id = str(uuid.uuid4())[:8]
    state = {"usia": req.usia, "gender": req.gender, "mood": MOOD_AWAL}

    try:
        updates = profile_agent.run(state)
        state.update(updates)
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Profile Agent error: {str(e)}")

    state["session_id"] = session_id
    sessions[session_id] = state

    return ProfileResponse(
        session_id=session_id,
        nama=state.get("nama", ""),
        background=state.get("background", ""),
        masalah_hari_ini=state.get("masalah_hari_ini", ""),
        ocean=state.get("ocean", {}),
        max_fails=state.get("max_fails", 2),
    )


@app.post("/generate_pesanan", response_model=DialogResponse)
def generate_pesanan(req: DialogRequest):
    """Generate dialog pemesanan NPC. Actor-Critic loop."""
    state = _get_session(req.session_id)
    state["dialog_state"] = "pesanan"
    state["konteks_critic"] = "pesanan"
    state["revisi_ke"] = 0

    try:
        state = jalankan_dialog_state(state, dialogue_agent.run_pesanan)
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Pesanan error: {str(e)}")

    sessions[req.session_id] = state

    return DialogResponse(
        session_id=req.session_id,
        dialog_state="pesanan",
        dialog_text=state.get("dialog_pesanan", ""),
        minuman_dipesan=state.get("minuman_dipesan", ""),
        critic_skor=state.get("critic_skor"),
        critic_lulus=state.get("critic_lulus"),
    )


@app.post("/generate_salah", response_model=DialogResponse)
def generate_salah(req: DialogRequest):
    """Generate dialog reaksi pesanan salah. Actor-Critic loop."""
    state = _get_session(req.session_id)
    state["dialog_state"] = "pesanan_salah"
    state["konteks_critic"] = "pesanan_salah"
    state["revisi_ke"] = 0

    try:
        state = jalankan_dialog_state(state, dialogue_agent.run_pesanan_salah)
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Salah error: {str(e)}")

    sessions[req.session_id] = state

    return DialogResponse(
        session_id=req.session_id,
        dialog_state="pesanan_salah",
        dialog_text=state.get("dialog_pesanan_salah", ""),
        critic_skor=state.get("critic_skor"),
        critic_lulus=state.get("critic_lulus"),
    )


@app.post("/generate_marah", response_model=DialogResponse)
def generate_marah(req: DialogRequest):
    """Generate dialog NPC marah dan pergi. Actor-Critic loop."""
    state = _get_session(req.session_id)
    state["dialog_state"] = "marah"
    state["konteks_critic"] = "marah"
    state["revisi_ke"] = 0

    try:
        state = jalankan_dialog_state(state, dialogue_agent.run_marah)
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Marah error: {str(e)}")

    sessions[req.session_id] = state

    return DialogResponse(
        session_id=req.session_id,
        dialog_state="marah",
        dialog_text=state.get("dialog_marah", ""),
        critic_skor=state.get("critic_skor"),
        critic_lulus=state.get("critic_lulus"),
    )


@app.post("/generate_berhasil", response_model=DialogResponse)
def generate_berhasil(req: DialogRequest):
    """Generate dialog NPC puas (pesanan benar). Actor-Critic loop."""
    state = _get_session(req.session_id)
    state["dialog_state"] = "berhasil"
    state["konteks_critic"] = "berhasil"
    state["revisi_ke"] = 0

    try:
        state = jalankan_dialog_state(state, dialogue_agent.run_berhasil)
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Berhasil error: {str(e)}")

    sessions[req.session_id] = state

    return DialogResponse(
        session_id=req.session_id,
        dialog_state="berhasil",
        dialog_text=state.get("dialog_berhasil", ""),
        critic_skor=state.get("critic_skor"),
        critic_lulus=state.get("critic_lulus"),
    )


@app.post("/start_curhat", response_model=DialogResponse)
def start_curhat(req: CurhatStartRequest):
    """
    Inisialisasi sesi curhat: set total ronde dan ronde pertama.
    Actor-Critic loop untuk ronde pertama.
    """
    state = _get_session(req.session_id)

    total = req.total_ronde if req.total_ronde else random_total_ronde()
    state["total_ronde"] = total
    state["ronde_sekarang"] = 1
    state["riwayat"] = []
    state["dialog_state"] = "curhat"
    state["konteks_critic"] = "curhat"
    state["revisi_ke"] = 0

    try:
        state = jalankan_dialog_state(state, dialogue_agent.run_curhat)
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Curhat error: {str(e)}")

    sessions[req.session_id] = state

    return DialogResponse(
        session_id=req.session_id,
        dialog_state="curhat",
        dialog_text=state.get("dialog_npc", ""),
        pilihan_jawaban=state.get("pilihan_jawaban", []),
        critic_skor=state.get("critic_skor"),
        critic_lulus=state.get("critic_lulus"),
        total_ronde=state.get("total_ronde", total),
        ronde_sekarang=state.get("ronde_sekarang", 1),
    )


@app.post("/next_curhat_round", response_model=DialogResponse)
def next_curhat_round(req: CurhatRoundRequest):
    """
    Lanjut ke ronde curhat berikutnya. Actor-Critic loop.
    """
    state = _get_session(req.session_id)
    total = state.get("total_ronde", 3)

    # Guard: jangan lewati batas ronde
    if req.ronde > total:
        raise HTTPException(
            status_code=400,
            detail=f"Sesi curhat sudah selesai ({total} ronde). Tidak ada ronde lagi."
        )

    state["ronde_sekarang"] = req.ronde
    state["dialog_state"] = "curhat"
    state["konteks_critic"] = "curhat"
    state["revisi_ke"] = 0

    try:
        state = jalankan_dialog_state(state, dialogue_agent.run_curhat)
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Curhat round error: {str(e)}")

    sessions[req.session_id] = state

    return DialogResponse(
        session_id=req.session_id,
        dialog_state="curhat",
        dialog_text=state.get("dialog_npc", ""),
        pilihan_jawaban=state.get("pilihan_jawaban", []),
        critic_skor=state.get("critic_skor"),
        critic_lulus=state.get("critic_lulus"),
        total_ronde=state.get("total_ronde", total),
        ronde_sekarang=state.get("ronde_sekarang", req.ronde),
    )


@app.post("/evaluate_answer", response_model=EvaluateResponse)
def evaluate_answer(req: EvaluateRequest):
    """
    Evaluasi jawaban pemain:
    1. Tentukan nada (satisfy/neutral/angry) — dari pilihan atau LLM
    2. Update mood
    3. Generate reaksi NPC
    """
    state = _get_session(req.session_id)
    state["jawaban_pemain"] = req.jawaban_pemain

    # Jika player memilih pilihan bernomor, set nadanya langsung
    if req.chosen_nada and req.chosen_nada in ("satisfy", "neutral", "angry"):
        # Inject ke pilihan_jawaban agar update_mood menemukannya
        pilihan = state.get("pilihan_jawaban", [])
        found = False
        for p in pilihan:
            if p["teks"] == req.jawaban_pemain:
                found = True
                break
        if not found:
            # Tambahkan sebagai pilihan virtual
            pilihan.append({"id": 99, "teks": req.jawaban_pemain, "nada": req.chosen_nada})
            state["pilihan_jawaban"] = pilihan

    mood_sebelum = state.get("mood", MOOD_AWAL)

    try:
        # 1) Update mood dan simpan riwayat
        mood_update = update_mood(state)
        state.update(mood_update)

        # 2) Generate reaksi NPC terhadap jawaban pemain
        reaksi_text = ""
        try:
            print(f"  [REAKSI] Generating reaksi untuk nada={state.get('_nada','?')}, mood={state.get('mood','?')}")
            reaksi_update = dialogue_agent.run_reaksi(state)
            state.update(reaksi_update)
            reaksi_text = state.get("reaksi_npc", "")
            print(f"  [REAKSI] Generated: {reaksi_text[:120] if reaksi_text else '(empty)'}")
        except Exception as re:
            import traceback
            print(f"  [WARNING] Gagal generate reaksi: {re}")
            traceback.print_exc()
            state["reaksi_npc"] = ""

        # 3) Patch riwayat terakhir dengan reaksi_npc yang sudah di-generate
        riwayat = state.get("riwayat", [])
        if riwayat and reaksi_text:
            riwayat[-1]["reaksi_npc"] = reaksi_text
            state["riwayat"] = riwayat

    except Exception as e:
        import traceback
        traceback.print_exc()
        raise HTTPException(status_code=500, detail=f"Evaluate error: {str(e)}")

    sessions[req.session_id] = state

    riwayat = state.get("riwayat", [])
    last_entry = riwayat[-1] if riwayat else {}

    return EvaluateResponse(
        session_id=req.session_id,
        nada=state.get("_nada", "neutral"),
        mood_sebelum=mood_sebelum,
        mood_sesudah=state.get("mood", mood_sebelum),
        reaksi_npc=state.get("reaksi_npc", ""),
        riwayat_entry=last_entry,
        total_ronde=state.get("total_ronde"),
        ronde_sekarang=state.get("ronde_sekarang"),
    )


@app.get("/session/{session_id}/summary", response_model=SessionSummaryResponse)
def get_session_summary(session_id: str):
    """Ambil ringkasan sesi curhat."""
    state = _get_session(session_id)
    return SessionSummaryResponse(
        session_id=session_id,
        nama=state.get("nama", ""),
        usia=state.get("usia", ""),
        gender=state.get("gender", ""),
        mood_awal=MOOD_AWAL,
        mood_akhir=state.get("mood", MOOD_AWAL),
        riwayat=state.get("riwayat", []),
    )


@app.delete("/session/{session_id}")
def delete_session(session_id: str):
    """Hapus session dari memory."""
    if session_id in sessions:
        del sessions[session_id]
        return {"status": "deleted", "session_id": session_id}
    raise HTTPException(status_code=404, detail="Session not found")


# ── Helper ────────────────────────────────────────────────────────────────────

def _get_session(session_id: str) -> dict:
    """Ambil session state, raise 404 jika tidak ditemukan."""
    if session_id not in sessions:
        raise HTTPException(status_code=404, detail=f"Session '{session_id}' not found. Generate profile first.")
    return sessions[session_id]


if __name__ == "__main__":
    print("=" * 60)
    print("  Tea'n Brew Multi-Agent API Server")
    print("  http://localhost:8765")
    print("  Docs: http://localhost:8765/docs")
    print("=" * 60)
    uvicorn.run(app, host="0.0.0.0", port=8765)
