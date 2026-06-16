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

import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))

from config     import GAYA_BAHASA
from llm_client import call_llm, parse_json
from state      import GameState


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

    system = """[Game Fiction Context — Tea'n Brew Visual Novel]
Tugas: merakit profil satu pelanggan café yang unik, realistis, dan relatable.

ATURAN WAJIB:
1. Nama: HANYA satu kata (nama panggilan). TANPA nama belakang.
   Contoh OK: Raka, Dimas, Sari, Bunga, Lesti, Fajar, Tari, Joko.
   Contoh SALAH: Raka Pratama, Dimas Putra.

2. Background: tepat 2 kalimat. Sertakan:
   - Apa yang dilakukan NPC sehari-hari (pekerjaan/status)
   - Satu fakta unik tentang kepribadiannya

3. Masalah hari ini: HARUS spesifik dan berbeda antar NPC. JANGAN pernah pakai masalah generik.
   Pilih SATU tema dari daftar ini:
   a) Tugas kuliah/kantor yang deadline-nya besok
   b) Pertengkaran dengan sahabat yang belum diselesaikan
   c) Bingung harus memilih A atau B dalam hidup
   d) Dimarahi orang tua tadi pagi tentang sesuatu
   e) Dompet kosong tapi harus bayar sesuatu minggu ini
   f) Putus cinta atau bertengkar dengan pacar
   g) capek banget tapi masih harus hadir ke acara
   h) Diejek atau diremehkan orang hari ini
   i) Pindah ke tempat baru dan belum punya teman
   j) Khawatir gagal ujian/tes/proyek penting
   Setelah memilih tema, JELASKAN secara spesifik — misalnya bukan "masalah kuliah" tapi "deadline skripsi yang dimajukan dosen padahal baru setengah jadi".

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
  "reaksi_gaya": "muka merah padam, bisik bilang 'ya udahlah' terus langsung pergi"
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

    raw    = call_llm(system, user)
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
