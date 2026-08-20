"""
server.py — FastAPI bridge antara Unity dan Multi-Agent system.

Unity mengirim HTTP request → server menjalankan agent → mengembalikan JSON response.

Endpoints:
  POST /generate_profile    → Profile Agent generates NPC
  POST /generate_pesanan    → Dialogue Agent: ordering dialog
  POST /generate_salah      → Dialogue Agent: wrong order reaction
  POST /generate_marah      → Dialogue Agent: angry & leaving
  POST /generate_berhasil   → Dialogue Agent: success reaction
    POST /start_curhat        → Dialogue Agent: venting round pertama
    POST /next_curhat_round   → Dialogue Agent: venting round berikutnya
    POST /evaluate_answer     → Mood update + venting reaction dialogue
    POST /generate_closing    → Dialogue Agent: venting closing dialogue
  GET  /health              → Health check

  ## Testing Endpoints (memanggil agent secara individu, tanpa loop):
  POST /test/profile        → Profile Agent saja (10x call untuk justifikasi)
  POST /test/dialogue       → Dialogue Agent saja (tanpa critic)
  POST /test/critic         → Critic Agent saja (lihat CoT + 8 dimensi FED)
"""

import os
import uuid
import uvicorn
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel, Field
from typing import Optional, List, Dict, Any

from config import MOOD_AWAL, MOOD_DELTA, MOOD_MIN, MOOD_MAX, MENU_MINUMAN
from graph import jalankan_dialog_state, update_mood, random_total_ronde
from llm_client import call_llm, parse_json

import agents.profile_agent  as profile_agent
import agents.dialogue_agent as dialogue_agent
import agents.critic_agent   as critic_agent

app = FastAPI(title="Tea'n Brew Multi-Agent API", version="1.0.0")

# ── CORS (wajib untuk WebGL / itch.io) ──────────────────────────────────────
# Atur origin spesifik via env agar aman:
#   CORS_ORIGINS="https://username.itch.io,https://mygame.vercel.app"
# Regex default mengizinkan subdomain itch.io.
raw_cors_origins = os.getenv("CORS_ORIGINS", "https://itch.io,https://www.itch.io")
cors_origins = [origin.strip() for origin in raw_cors_origins.split(",") if origin.strip()]
cors_origin_regex = os.getenv(
    "CORS_ALLOW_ORIGIN_REGEX",
    r"^https://([a-zA-Z0-9-]+\.)*(itch\.io|itch\.zone|hwcdn\.net)$",
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=cors_origins,
    allow_origin_regex=cors_origin_regex,
    allow_credentials=False,
    allow_methods=["GET", "POST", "DELETE", "OPTIONS"],
    allow_headers=["*"],
    expose_headers=["*"],
    max_age=600,
)

# ── Request Logging Middleware ────────────────────────────────────────────────
from fastapi import Request
import time

@app.middleware("http")
async def log_requests(request: Request, call_next):
    """Log semua HTTP request ke terminal."""
    start_time = time.time()
    
    # Log incoming request
    method = request.method
    path = request.url.path
    print(f"\n{'='*60}")
    print(f"  >>> {method} {path}")
    
    # Log body untuk POST/PUT/PATCH
    if method in ["POST", "PUT", "PATCH"]:
        try:
            body = await request.body()
            if body:
                # Parse untuk tampilan lebih bersih
                import json
                try:
                    body_json = json.loads(body)
                    print(f"  >>> Body: {json.dumps(body_json, indent=2)[:500]}")
                except:
                    print(f"  >>> Body: {body[:200]}")
        except:
            pass
    
    # Process request
    response = await call_next(request)
    
    # Log response
    duration = (time.time() - start_time) * 1000
    print(f"  <<< {method} {path} → {response.status_code} ({duration:.0f}ms)")
    print(f"{'='*60}")
    
    return response

# ── In-memory session storage ─────────────────────────────────────────────────
# Key: session_id, Value: GameState dict
sessions: Dict[str, dict] = {}


# ── Request / Response Models ─────────────────────────────────────────────────

class ProfileRequest(BaseModel):
    usia: str = Field(..., description="Kategori usia: remaja/dewasa/orang tua")
    gender: str = Field(..., description="Gender: pria/wanita")

class ProfileResponse(BaseModel):
    session_id: str
    nama: str
    background: str
    masalah_hari_ini: str
    ocean: dict
    max_fails: int

class DialogRequest(BaseModel):
    session_id: str
    minuman_dipesan: Optional[str] = Field(None, description="Minuman yang sudah dipilih Unity (untuk pesanan)")

class DialogResponse(BaseModel):
    session_id: str
    dialog_state: str
    dialog_text: str
    minuman_dipesan: Optional[str] = None
    pilihan_jawaban: Optional[List[dict]] = None
    critic_skor: Optional[float] = None
    critic_lulus: Optional[bool] = None
    critic_log: Optional[List[dict]] = None
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
    critic_skor: Optional[float] = None
    critic_lulus: Optional[bool] = None
    critic_log: Optional[List[dict]] = None

class SessionSummaryResponse(BaseModel):
    session_id: str
    nama: str
    usia: str
    gender: str
    mood_awal: int
    mood_akhir: int
    riwayat: list


def _resolve_total_ronde(requested: Optional[int]) -> int:
    """Resolve jumlah ronde; 0 atau null berarti gunakan rentang acak konfigurasi."""
    if requested is not None and requested > 0:
        return requested
    return random_total_ronde()


def _ensure_total_ronde(state: dict) -> int:
    """Pastikan session selalu memiliki jumlah ronde yang valid."""
    total = state.get("total_ronde")
    if not total or total < 1:
        total = random_total_ronde()
        state["total_ronde"] = total
    return total

# ── Testing Models ────────────────────────────────────────────────────────────

class TestProfileRequest(BaseModel):
    usia: str = Field("remaja", description="Kategori usia")
    gender: str = Field("wanita", description="Gender")
    jumlah: int = Field(10, description="Jumlah panggilan agent", ge=1, le=50)

class TestProfileResponse(BaseModel):
    jumlah: int
    hasil: List[dict]

class TestDialogueRequest(BaseModel):
    usia: str = Field("remaja", description="Kategori usia")
    gender: str = Field("wanita", description="Gender")
    state: str = Field("pesanan", description="Dialog state: pesanan/pesanan_salah/marah/berhasil/curhat")

class TestDialogueResponse(BaseModel):
    state: str
    dialog: dict
    raw: Optional[str] = Field(None, description="Raw LLM output (opsional)")

class TestCriticRequest(BaseModel):
    """Kirim dialog manual untuk dievaluasi Critic Agent."""
    usia: str = Field("remaja")
    gender: str = Field("wanita")
    konteks: str = Field("pesanan", description="State: pesanan/pesanan_salah/marah/berhasil/curhat")
    ocean: dict = Field(..., description="OCEAN scores 0-100")
    nama: str = Field("Test NPC")
    dialog: dict = Field(..., description="Dialog yang dievaluasi (sesuai state)")

class TestCriticResponse(BaseModel):
    lulus: bool
    skor: float
    skor_dimensi: dict  # 8 dimensi FED
    justifikasi: str     # Chain-of-Thought
    catatan: list
    saran: str


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

    # Minuman sudah dipilih oleh Unity, simpan ke state
    if req.minuman_dipesan:
        state["minuman_dipesan"] = req.minuman_dipesan

    try:
        state = jalankan_dialog_state(state, dialogue_agent.run_pesanan)
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Pesanan error: {str(e)}")

    # FORCE: minuman_dipesan HARUS sesuai yang dikirim Unity, tidak boleh di-overwrite
    if req.minuman_dipesan:
        state["minuman_dipesan"] = req.minuman_dipesan
        # Juga replace di dialog text jika LLM hallucinate minuman lain
        dialog_text = state.get("dialog_pesanan", "")
        if req.minuman_dipesan.lower() not in dialog_text.lower():
            # Cari nama minuman yang salah di dialog dan ganti
            import re
            for old_drink in ["green tea", "black tea", "mint tea", "matcha latte", "jasmine tea", "chamomile tea", "oolong tea"]:
                if old_drink.lower() != req.minuman_dipesan.lower() and old_drink.lower() in dialog_text.lower():
                    dialog_text = re.sub(old_drink, req.minuman_dipesan, dialog_text, flags=re.IGNORECASE)
                    state["dialog_pesanan"] = dialog_text
                    break

    sessions[req.session_id] = state

    return DialogResponse(
        session_id=req.session_id,
        dialog_state="pesanan",
        dialog_text=state.get("dialog_pesanan", ""),
        minuman_dipesan=state.get("minuman_dipesan", ""),
        critic_skor=state.get("critic_skor"),
        critic_lulus=state.get("critic_lulus"),
        critic_log=state.get("critic_log"),
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
        critic_log=state.get("critic_log"),
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
        critic_log=state.get("critic_log"),
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
        critic_log=state.get("critic_log"),
    )


@app.post("/start_curhat", response_model=DialogResponse)
def start_curhat(req: CurhatStartRequest):
    """
    Inisialisasi sesi curhat: set total ronde dan ronde pertama.
    Actor-Critic loop untuk ronde pertama.
    """
    state = _get_session(req.session_id)

    total = _resolve_total_ronde(req.total_ronde)
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
        critic_log=state.get("critic_log"),
        total_ronde=state.get("total_ronde") or total,
        ronde_sekarang=state.get("ronde_sekarang", 1),
    )


@app.post("/next_curhat_round", response_model=DialogResponse)
def next_curhat_round(req: CurhatRoundRequest):
    """
    Lanjut ke ronde curhat berikutnya. Actor-Critic loop.
    """
    state = _get_session(req.session_id)
    total = state.get("total_ronde") or random_total_ronde()
    state["total_ronde"] = total

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
        critic_log=state.get("critic_log"),
        total_ronde=state.get("total_ronde") or total,
        ronde_sekarang=state.get("ronde_sekarang", req.ronde),
    )


@app.post("/generate_closing", response_model=DialogResponse)
def generate_closing(req: DialogRequest):
    """
    Generate dialog penutup sesi curhat setelah semua ronde selesai.
    Memanggil Dialogue Agent untuk membuat closing yang kontekstual.
    """
    state = _get_session(req.session_id)
    state["dialog_state"] = "closing"
    state["konteks_critic"] = "closing"
    state["revisi_ke"] = 0

    try:
        print(f"  [CLOSING] Generating closing untuk session {req.session_id}")
        state = jalankan_dialog_state(state, dialogue_agent.run_closing)
        print(f"  [CLOSING] OK: {state.get('dialog_closing', '')[:100]}")
    except Exception as e:
        import traceback
        traceback.print_exc()
        raise HTTPException(status_code=500, detail=f"Closing error: {str(e)}")

    sessions[req.session_id] = state

    return DialogResponse(
        session_id=req.session_id,
        dialog_state="closing",
        dialog_text=state.get("dialog_closing", ""),
        pilihan_jawaban=None,  # Closing tidak punya pilihan
        critic_skor=None,
        critic_lulus=None,
        total_ronde=_ensure_total_ronde(state),
        ronde_sekarang=state.get("ronde_sekarang"),
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
            state["dialog_state"] = "reaksi"
            state["konteks_critic"] = "reaksi"
            state["revisi_ke"] = 0
            state = jalankan_dialog_state(state, dialogue_agent.run_reaksi)
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
        total_ronde=_ensure_total_ronde(state),
        ronde_sekarang=state.get("ronde_sekarang"),
        critic_skor=state.get("critic_skor"),
        critic_lulus=state.get("critic_lulus"),
        critic_log=state.get("critic_log"),
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


# ── TESTING ENDPOINTS ─────────────────────────────────────────────────────────
# Endpoint ini memanggil agent SECARA INDIVIDU (tanpa Actor-Critic loop)
# untuk keperluan justifikasi, debugging, dan melihat proses berpikir agent.

@app.post("/test/profile", response_model=TestProfileResponse)
def test_profile(req: TestProfileRequest):
    """
    TEST: Profile Agent — panggil N kali untuk justifikasi konsistensi.
    Tidak membuat session, tidak menyimpan state.
    Return semua hasil generasi.
    """
    hasil = []
    for i in range(req.jumlah):
        state = {"usia": req.usia, "gender": req.gender, "mood": MOOD_AWAL}
        try:
            updates = profile_agent.run(state)
            state.update(updates)
            hasil.append({
                "ke": i + 1,
                "nama": state.get("nama", ""),
                "background": state.get("background", ""),
                "masalah_hari_ini": state.get("masalah_hari_ini", ""),
                "ocean": state.get("ocean", {}),
                "max_fails": state.get("max_fails", 2),
            })
        except Exception as e:
            hasil.append({"ke": i + 1, "error": str(e)})

    return TestProfileResponse(jumlah=req.jumlah, hasil=hasil)


@app.post("/test/dialogue", response_model=TestDialogueResponse)
def test_dialogue(req: TestDialogueRequest):
    """
    TEST: Dialogue Agent saja — generate dialog TANPA critic.
    Membuat profile dulu (sekali), lalu panggil dialogue agent sesuai state.
    Tidak ada Actor-Critic loop, tidak ada revisi.
    """
    # Generate profile sementara
    state = {"usia": req.usia, "gender": req.gender, "mood": MOOD_AWAL}
    try:
        updates = profile_agent.run(state)
        state.update(updates)
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Profile error: {str(e)}")

    state["dialog_state"] = req.state
    state["konteks_critic"] = req.state
    state["revisi_ke"] = 0

    # Panggil dialogue agent sesuai state
    dialogue_fn = {
        "pesanan": dialogue_agent.run_pesanan,
        "pesanan_salah": dialogue_agent.run_pesanan_salah,
        "marah": dialogue_agent.run_marah,
        "berhasil": dialogue_agent.run_berhasil,
        "curhat": dialogue_agent.run_curhat,
    }.get(req.state)

    if not dialogue_fn:
        raise HTTPException(status_code=400, detail=f"Unknown state: {req.state}")

    try:
        updates = dialogue_fn(state)
        state.update(updates)
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Dialogue error: {str(e)}")

    # Ambil dialog sesuai state
    dialog_map = {
        "pesanan": {"dialog_pesanan": state.get("dialog_pesanan", ""),
                    "minuman_dipesan": state.get("minuman_dipesan", "")},
        "pesanan_salah": {"dialog_pesanan_salah": state.get("dialog_pesanan_salah", "")},
        "marah": {"dialog_marah": state.get("dialog_marah", "")},
        "berhasil": {"dialog_berhasil": state.get("dialog_berhasil", "")},
        "curhat": {"dialog_npc": state.get("dialog_npc", ""),
                   "pilihan_jawaban": state.get("pilihan_jawaban", [])},
    }

    return TestDialogueResponse(
        state=req.state,
        dialog=dialog_map.get(req.state, {}),
    )


@app.post("/test/critic", response_model=TestCriticResponse)
def test_critic(req: TestCriticRequest):
    """
    TEST: Critic Agent saja — evaluasi dialog manual dengan G-Eval + FED.
    Lihat proses berpikir (Chain-of-Thought) dan 8 dimensi FED.
    Tidak ada session, tidak ada revisi.
    """
    # Build state minimal untuk critic
    state = {
        "usia": req.usia,
        "gender": req.gender,
        "nama": req.nama,
        "ocean": req.ocean,
        "konteks_critic": req.konteks,
        "revisi_ke": 0,
        "mood": 50,
    }

    # Inject dialog sesuai konteks
    state.update(req.dialog)

    try:
        result = critic_agent.run(state)
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Critic error: {str(e)}")

    return TestCriticResponse(
        lulus=result.get("critic_lulus", False),
        skor=result.get("critic_skor", 0),
        skor_dimensi=result.get("critic_skor_dimensi", {}),
        justifikasi=result.get("critic_justifikasi", ""),
        catatan=result.get("critic_catatan", []),
        saran=result.get("critic_saran", ""),
    )


# ── Entry Point ────────────────────────────────────────────────────────────────

if __name__ == "__main__":
    import uvicorn
    print("Starting Tea'n Brew Multi-Agent API on http://localhost:8765")
    uvicorn.run(app, host="0.0.0.0", port=8765, log_level="info")
