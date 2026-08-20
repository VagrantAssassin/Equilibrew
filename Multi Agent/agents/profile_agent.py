"""
agents/profile_agent.py
-----------------------
Profile Agent — dipanggil saat pelanggan baru datang ke kafe.

Tanggung jawab:
  Menghasilkan profil lengkap NPC secara dinamis berdasarkan kategori
  usia dan gender, mencakup:
    - Nama khas Indonesia sesuai usia/gender
    - Background kehidupan (2 kalimat)
    - Masalah spesifik yang dibawa hari ini
    - Skor kepribadian OCEAN (0–100 per dimensi)
    - max_fails: toleransi kesalahan pesanan (1–3)

  Output langsung menggantikan mekanik randomize_profile di game Unity.
"""

import sys, os, random, time
sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))

from config     import (
    GAYA_BAHASA, PROFILE_MODEL,
    PROFILE_TEMPERATURE, PROFILE_FREQUENCY_PENALTY, PROFILE_PRESENCE_PENALTY,
)
from llm_client import call_llm, parse_json
from state      import GameState

# ── Pool nama untuk variasi (mencegah reasoning model konvergen ke nama sama) ──
_NAMA_POOL_PRIA = [
    "Raka", "Dimas", "Fajar", "Joko", "Reza", "Adit", "Bayu", "Galih", "Hadi",
    "Iwan", "Krisna", "Lukman", "Marco", "Nico", "Oscar", "Pandu", "Rizki",
    "Surya", "Teguh", "Yoga", "Zaki", "Arman", "Budi", "Candra", "Dwi",
    "Eko", "Farhan", "Gunawan", "Hendra", "Ilham",
]
_NAMA_POOL_WANITA = [
    "Sari", "Bunga", "Lesti", "Tari", "Anya", "Citra", "Dewi", "Elsa", "Fitri",
    "Gita", "Hana", "Indah", "Jihan", "Kania", "Lala", "Maya", "Nisa", "Olivia",
    "Putri", "Rani", "Sasa", "Tika", "Ulya", "Vina", "Wati", "Yuni", "Zahra",
    "Amelia", "Bella", "Clara", "Diana", "Era", "Fiona",
]

# ── Tema masalah untuk dipilih random (memaksa variasi antar pelanggan) ──
_TEMA_MASALAH = [
    ("Tugas kuliah/kantor yang deadline-nya besok", "akademik/pekerjaan"),
    ("Pertengkaran dengan sahabat yang belum diselesaikan", "sosial"),
    ("Bingung harus memilih A atau B dalam hidup", "keputusan"),
    ("Dimarahi orang tua tadi pagi tentang sesuatu", "keluarga"),
    ("Dompet kosong tapi harus bayar sesuatu minggu ini", "finansial"),
    ("Putus cinta atau bertengkar dengan pacar", "percintaan"),
    ("Capek banget tapi masih harus hadir ke acara", "kelelahan"),
    ("Diejek atau diremehkan orang hari ini", "harga diri"),
    ("Pindah ke tempat baru dan belum punya teman", "transisi"),
    ("Khawatir gagal ujian/tes/proyek penting", "kecemasan"),
]


def build_ocean_desc(ocean: dict) -> str:
    """Terjemahkan skor OCEAN ke deskripsi naratif untuk prompt."""
    return "\n".join([
        f"- Openness {ocean['openness']}/100: " +
            ("Imajinatif, kreatif, suka hal baru" if ocean['openness'] > 60
             else "Konservatif, praktis, tidak suka perubahan"),
        f"- Conscientiousness {ocean['conscientiousness']}/100: " +
            ("Terstruktur, teliti, bertanggung jawab" if ocean['conscientiousness'] > 60
             else "Santai, fleksibel, kadang ceroboh"),
        f"- Extraversion {ocean['extraversion']}/100: " +
            ("Cerewet, terbuka, mudah akrab dengan orang asing" if ocean['extraversion'] > 60
             else "Pendiam, tertutup, perlu waktu untuk terbuka"),
        f"- Agreeableness {ocean['agreeableness']}/100: " +
            ("Empatis, lembut, mudah mengalah" if ocean['agreeableness'] > 60
             else "Blak-blakan, tegas, tidak mudah menerima pendapat orang"),
        f"- Neuroticism {ocean['neuroticism']}/100: " +
            ("Sensitif, mudah cemas, emosional" if ocean['neuroticism'] > 60
             else "Stabil, tenang, tidak mudah terbawa perasaan"),
    ])


def run(state: GameState) -> dict:
    """
    Node LangGraph: profile_agent
    Dipanggil di awal saat pelanggan baru tiba (skenario Customer Came).

    Input : state["usia"], state["gender"]
    Output: nama, background, masalah_hari_ini, ocean, max_fails
    """
    usia   = state["usia"]
    gender = state["gender"]

    # ── Randomisasi untuk mencegah reasoning model konvergen ke output sama ──
    # Reasoning model (GPT-5.6-luna) mengabaikan temperature, jadi kita inject
    # elemen acak ke prompt supaya setiap pelanggan benar-benar berbeda.
    seed = int(time.time() * 1000) % 100000
    random.seed(seed)

    # Pilih tema masalah secara random
    tema_idx = random.randint(0, len(_TEMA_MASALAH) - 1)
    tema_label, tema_kategori = _TEMA_MASALAH[tema_idx]

    # Pilih 3 nama acak dari pool sebagai "inspirasi" (gender-appropriate)
    if gender == "wanita":
        nama_inspirasi = random.sample(_NAMA_POOL_WANITA, min(3, len(_NAMA_POOL_WANITA)))
    else:
        nama_inspirasi = random.sample(_NAMA_POOL_PRIA, min(3, len(_NAMA_POOL_PRIA)))

    # Pilih rentang OCEAN acak sebagai "arah kepribadian" supaya tidak sama semua
    ocean_hints = random.sample([
        "introvert pendiam", "extrovert cerewet", "sangat emosional",
        "tenang dan stabil", "blak-blakan", "sangat ramah", "kreatif/imajinatif",
        "praktis/lugas", "cemas dan overthinking", "santai dan flexible",
    ], 3)

    # Seed unik untuk session ini
    session_tag = f"SEED-{seed}"

    system = """[Game Fiction Context — Tea'n Brew Visual Novel]
Tugas: merakit profil satu pelanggan café yang unik, realistis, dan relatable.

ATURAN WAJIB:
1. Nama: HANYA satu kata (nama panggilan). TANPA nama belakang.
   PILIH NAMA DARI DAFTAR INSPIRASI yang diberikan di user message.
   JANGAN pakai nama yang sama berulang-ulang. Variasikan setiap pelanggan.
   Contoh OK: Raka, Dimas, Sari, Bunga, Lesti, Fajar, Tari, Joko.
   Contoh SALAH: Raka Pratama, Dimas Putra.

2. Background: tepat 2 kalimat. Sertakan:
   - Apa yang dilakukan NPC sehari-hari (pekerjaan/status)
   - Satu fakta unik tentang kepribadiannya

3. Masalah hari ini: HARUS spesifik dan berbeda antar NPC. JANGAN pernah pakai masalah generik.
   TEMA MASALAH SUDAH DITENTUKAN di user message — WAJIB pakai tema tersebut.
   JELASKAN secara spesifik — misalnya bukan "masalah kuliah" tapi "deadline skripsi yang dimajukan dosen padahal baru setengah jadi".

4. OCEAN (0-100): buat skor yang KONSISTEN. Contoh:
   - Introvert (E<40) + Neurotic tinggi → masalah sosial terasa lebih berat
   - Extrovert (E>60) + Agreeable tinggi → masalahnya tentang orang lain, bukan diri sendiri
   - Openness tinggi → masalahnya kreatif/unik, bukan rutinitas

5. max_fails (1-3): dipengaruhi Agreeableness + Neuroticism:
   - A tinggi + N rendah → max_fails=3 (sabar)
   - A rendah + N tinggi → max_fails=1 (cepat marah)
   - Sisanya → max_fails=2

6. reaksi_gaya: pilih SATU cara NPC bereaksi saat marah dan saat puas.

Kembalikan HANYA JSON valid, tanpa markdown, tanpa teks lain."""

    user = f"""Pelanggan baru memasuki kafe. Buat profilnya:
- Kategori usia : {usia}
- Gender        : {gender}
- Gaya bahasa nanti: {GAYA_BAHASA.get(usia, 'natural')}

VARIASI WAJIB (tag unik: {session_tag}):
- NAMA: pilih dari inspirasi berikut (boleh variasi, asal satu kata): {", ".join(nama_inspirasi)}
- TEMA MASALAH (WAJIB pakai tema ini): {tema_label} (kategori: {tema_kategori})
- ARAH KEPRIBADIAN: kombinasi dari -> {", ".join(ocean_hints)}

PENTING: Setiap pelanggan HARUS unik. Jangan ulang nama atau masalah yang sama.

Contoh profil BAIK:
{{
  "nama": "Raka",
  "background": "Mahasiswa teknik yang hobi main gitar di kosan. Tipikal orang yang keliatan cuek tapi sebenernya perhatian banget.",
  "masalah_hari_ini": "Deadline tugas kelompok dimajukan jadi besok padahal anggota timnya pada menghilang semua, bingung harus mulai dari mana.",
  "ocean": {{ "openness": 62, "conscientiousness": 55, "extraversion": 68, "agreeableness": 47, "neuroticism": 61 }},
  "max_fails": 2,
  "reaksi_gaya": "langsung ngomong keras dan minta bicara sama manajernya"
}}

Contoh profil LAINNYA (jangan duplikat):
{{
  "nama": "Sari",
  "background": "Barista magang yang baru kerja seminggu. Masih suka nervous kalau ada pelanggan marah.",
  "masalah_hari_ini": "Ketahuan bossnya suka main HP pas kerja dan sekarang dipanggil masuk ruangan buat dimarahin.",
  "ocean": {{ "openness": 45, "conscientiousness": 38, "extraversion": 52, "agreeableness": 72, "neuroticism": 75 }},
  "max_fails": 1,
  "reaksi_gaya": "langsung pergi tanpa banyak bicara, cuma bilang 'ya udahlah' pelan"
}}

Format JSON:
{{
  "nama": "satu kata saja",
  "background": "kalimat 1. kalimat 2.",
  "masalah_hari_ini": "masalah SPESIFIK yang dibawa hari ini (bukan generik)",
  "ocean": {{
    "openness": <0-100>,
    "conscientiousness": <0-100>,
    "extraversion": <0-100>,
    "agreeableness": <0-100>,
    "neuroticism": <0-100>
  }},
  "max_fails": <1, 2, atau 3>,
  "reaksi_gaya": "1 cara NPC bereaksi saat marah"
}}"""

    raw    = call_llm(
        system, user, model=PROFILE_MODEL,
        temperature=PROFILE_TEMPERATURE,
        frequency_penalty=PROFILE_FREQUENCY_PENALTY,
        presence_penalty=PROFILE_PRESENCE_PENALTY,
    )
    result = parse_json(raw)

    return {
        "nama"            : result["nama"],
        "background"      : result["background"],
        "masalah_hari_ini": result["masalah_hari_ini"],
        "ocean"           : result["ocean"],
        "max_fails"       : result.get("max_fails", 2),
        "reaksi_gaya"     : result.get("reaksi_gaya", ""),
        # Init state
        "fail_count"      : 0,
        "dialog_state"    : "pesanan",
        "mood"            : 50,   # MOOD_AWAL — nanti dari Unity
        "riwayat"         : [],
        "revisi_ke"       : 0,
        "konteks_critic"  : "pesanan",
    }
