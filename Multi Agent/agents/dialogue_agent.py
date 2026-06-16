"""
agents/dialogue_agent.py
------------------------
Dialogue Agent — bertanggung jawab atas SEMUA produksi dialog NPC.

Menangani 5 dialog state sesuai skenario game Tea'n Brew:
  run_pesanan()       → state "pesanan"       : NPC memesan minuman
  run_pesanan_salah() → state "pesanan_salah" : NPC bereaksi saat pesanan salah
  run_marah()         → state "marah"         : NPC marah dan pergi (max fails)
  run_berhasil()      → state "berhasil"      : NPC puas, pesanan benar
  run_curhat()        → state "curhat"        : Dialog curhat multi-baris per ronde
  run_reaksi()        → sub-state curhat      : Reaksi NPC setelah pemain menjawab
"""

import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(__file__)))

from config     import GAYA_BAHASA, MENU_MINUMAN
from llm_client import call_llm, parse_json
from state      import GameState
from agents.profile_agent import build_ocean_desc


def _mood_ctx(mood: int) -> str:
    """Konteks mood yang lebih granular untuk mengarahkan tone dialog NPC."""
    if mood >= 80:
        return (
            "NPC SANGAT SENANG dan hangat. Berbicara dengan antusias, banyak emoji verbal "
            "(ah, wah, ih), suka pakai kata pujian, sangat terbuka dan ceria. "
            "Senyum lebar, nada tinggi, banyak ekspresi positif."
        )
    elif mood >= 60:
        return (
            "NPC senang dan cukup terbuka. Dialog ramah, suka bercanda sedikit, "
            "nada ceria tapi tidak berlebihan. Masih mau lanjut cerita."
        )
    elif mood >= 40:
        return (
            "NPC dalam kondisi netral. Bercerita apa adanya tanpa antusiasme berlebih. "
            "Dialog biasa, tidak terlalu panjang, tidak terlalu pendek."
        )
    elif mood >= 20:
        return (
            "NPC KESAL dan mulai defensif. Dialog lebih pendek, nada mulai dingin, "
            "suka memotong pembicaraan, kadang sindiran halus. "
            "Tidak banyak tersenyum, ekspresi datar atau cemberut."
        )
    else:
        return (
            "NPC SANGAT MARAH atau kecewa berat. Dialog SANGAT pendek (1-2 kalimat), "
            "nada tajam, sinis, atau pasif-agresif. Ingin segera menutup percakapan. "
            "Kadang diam saja, kadang meledak. TIDAK mau diajak santai."
        )


def _riwayat_ctx(riwayat: list) -> str:
    if not riwayat:
        return ""
    lines = ["\nRiwayat percakapan sebelumnya (dari ronde 1 sampai sekarang):"]
    for r in riwayat:
        lines.append(
            f"  [Ronde {r['ronde']}]"
            f"\n    NPC: \"{r['dialog_npc'][:120]}\""
            f"\n    Pemain: \"{r['jawaban_pemain']}\" (nada={r['nada']})"
        )
    # ── Ekstrak data/fakta yang sudah disebut supaya LLM tidak ngulang ──
    all_text = " ".join(r['dialog_npc'] for r in riwayat)
    mentioned_keywords = []
    for r in riwayat:
        npc_text = r['dialog_npc']
        # Ambil kata kunci unik dari setiap ronde (10 kata pertama yang bermakna)
        words = [w for w in npc_text.split() if len(w) > 3]
        mentioned_keywords.extend(words[:8])
    unique_keywords = list(dict.fromkeys(mentioned_keywords))[:25]  # dedupe, max 25
    if unique_keywords:
        lines.append(f"\nDATA/FAKTA YANG SUDAH DISEBUT (JANGAN ULANGI): {', '.join(unique_keywords)}")
        lines.append("INFO INI PENTING: Ronde baru HARUS membahas ASPEK BARU yang BELUM diceritakan.")
    return "\n".join(lines)


def _base_system(ocean: dict, usia: str) -> str:
    """Build system prompt yang TRANSLATE OCEAN scores ke perilaku bicara konkret."""

    # ── Translate OCEAN ke pola bicara yang terlihat ──
    e = ocean.get('extraversion', 50)
    n = ocean.get('neuroticism', 50)
    a = ocean.get('agreeableness', 50)
    o = ocean.get('openness', 50)
    c = ocean.get('conscientiousness', 50)

    # Extraversion → panjang dialog & antusiasme
    if e >= 70:
        ekspresi_e = "Panjang ceritanya, banyak interjeksi (ih, wah, eh), suka menambahkan detail yang tidak diminta, bersemangat."
    elif e >= 40:
        ekspresi_e = "Cerita dengan proporsional, ada detail tapi tidak berlebihan. Normal."
    else:
        ekspresi_e = "Pendiam. Dialog singkat (1-3 kalimat), tidak banyak detail, terkesan menahan diri. Kadang hanya mengangguk atau diam."

    # Neuroticism → intensitas emosi
    if n >= 70:
        ekspresi_n = "Emosional. Mudah terbawa perasaan, nada berubah cepat (dari senang ke sedih atau sebaliknya). Sering pakai kata 'banget', 'parah', 'ih serius'. Ekspresi berlebihan."
    elif n >= 40:
        ekspresi_n = "Stabil tapi masih punya emosi. Normal."
    else:
        ekspresi_n = "Tenang dan stabil. Tidak mudah terpancing. Nada datar tapi bukan dingin. Bahkan saat marah, tetap terkendali."

    # Agreeableness → kelembutan & konflik
    if a >= 70:
        ekspresi_a = "Sangat ramah, sulit marah, selalu mencoba memaklumi. Kalau kecewa, lebih ke sedih daripada marah. Suka minta maaf duluan meski bukan salahnya."
    elif a >= 40:
        ekspresi_a = "Normal. Ramah tapi punya batas."
    else:
        ekspresi_a = "Blak-blakan, frontal. Kalau tidak suka, langsung bilang. Tidak basa-basi. Bisa terkesan kasar tapi sebenarnya jujur."

    # Openness → variasi kosakata & kreativitas
    if o >= 70:
        ekspresi_o = "Kosakata kreatif, suka metafora atau perumpamaan. Berpikir di luar kebiasaan. Kadang filosofis."
    else:
        ekspresi_o = "Bahasa lugas dan praktis. Tidak banyak metafora. Langsung ke inti."

    return f"""[Game Fiction Context - Tea'n Brew Visual Novel]
Tugas: menghasilkan dialog NPC yang AUTENTIK dan TERASA HIDUP berdasarkan kepribadian OCEAN-nya.

KEPRIBADIAN NPC (DITERJEMAHKAN ke perilaku bicara):
{build_ocean_desc(ocean)}

POLA BICARA NPC BERDASARKAN OCEAN:
- Kekuatan/Ekspresi : {ekspresi_e}
- Emosional         : {ekspresi_n}
- Kelembutan/Konflik : {ekspresi_a}
- Kreativitas bahasa: {ekspresi_o}

Gaya bahasa usia ({usia}): {GAYA_BAHASA.get(usia, 'natural')}

ATURAN MUTLAK:
1. NPC memanggil pemain "kak". JANGAN "bro", "man", "sis", "guys".
2. Dialog harus TERASA BEDA antar NPC. Jangan semua NPC bicara dengan nada yang sama.
3. Tunjukkan OCEAN melalui CARA BICARA, bukan dengan menyebutkan angka atau sifat langsung.
4. Kalimat harus natural seperti orang bicara sungguhan — ada jeda, ada emosi, ada gaya.
5. Untuk state curhat: NPC wajib merespons jawaban pemain di ronde sebelumnya, jangan mulai dari awal.

Kembalikan HANYA JSON valid, tanpa teks lain."""


# ── STATE 1: Pesanan ──────────────────────────────────────────────────────────

def run_pesanan(state: GameState) -> dict:
    """NPC datang dan memesan minuman."""
    ocean  = state["ocean"]
    system = _base_system(ocean, state["usia"])

    user = f"""NPC: {state['nama']} ({state['usia']}, {state['gender']})
Background   : {state['background']}
Masalah hari ini: {state['masalah_hari_ini']}
Menu tersedia: {', '.join(MENU_MINUMAN)}

Buat dialog NPC PERTAMA KALI datang ke café dan memesan minuman.

PANDUAN:
- NPC sedang membawa beban masalah hari ini, jadi dialog pesanannya boleh terpengaruh suasana hati
- Misalnya: kalau lagi sedih mungkin pesannya sambil mendesah, kalau lagi marah mungkin pesannya to the point
- Tapi JANGAN langsung cerita masalah — cukup terlihat dari nada atau ekspresi kecil
- Pilih minuman dari menu yang sesuai suasana hati NPC
- 1-3 kalimat, natural, sesuai pola bicara OCEAN di atas
- Panggil pemain "kak" (1 kali saja)

Format JSON:
{{
  "minuman_dipesan": "nama minuman dari menu",
  "dialog_pesanan": "dialog NPC memesan..."
}}"""

    raw    = call_llm(system, user)
    result = parse_json(raw)

    return {
        "minuman_dipesan": result["minuman_dipesan"],
        "dialog_pesanan" : result["dialog_pesanan"],
        "dialog_state"   : "pesanan",
        "konteks_critic" : "pesanan",
        "revisi_ke"      : 0,
    }


# ── STATE 2: Pesanan Salah ────────────────────────────────────────────────────

def run_pesanan_salah(state: GameState) -> dict:
    """NPC bereaksi saat pemain menyajikan minuman yang salah."""
    ocean      = state["ocean"]
    fail_count = state.get("fail_count", 1)
    max_fails  = state.get("max_fails", 2)
    system     = _base_system(ocean, state["usia"])
    reaksi_gaya = state.get("reaksi_gaya", "")

    # Intensitas berdasarkan progres fail
    if fail_count == 1:
        intensitas = "Sedikit kecewa. Masih mau maklumi, mungkin hanya salah dengar."
    elif fail_count == 2:
        intensitas = "Cukup kesal. Sudah mulai hilang sabar, nada mulai tinggi."
    else:
        intensitas = "SANGAT kesal. Hampir marah. Hampir menyerah."

    # Gaya reaksi yang konsisten dengan profil
    gaya_hint = f"\nGaya reaksi marah NPC ini: {reaksi_gaya}" if reaksi_gaya else ""

    user = f"""NPC: {state['nama']} ({state['usia']}, {state['gender']})
Pesanan NPC: {state.get('minuman_dipesan', 'teh')}
Kesalahan ke-{fail_count} dari maksimal {max_fails} kali.
Intensitas: {intensitas}{gaya_hint}

Pemain menyajikan minuman yang SALAH.

CARA NPC BEREAKSI BERDASARKAN KEPALIBADIAN:
- Kalau Agreeableness tinggi: kecewanya lembut, lebih ke sedih dari marah. "ih kak, ini bukan yang aku pesen..."
- Kalau Agreeableness rendah: frontal dan blak-blakan. "eh kok salah? yang bener dong!"
- Kalau Neuroticism tinggi: reaksi berlebihan, dramatis. "parah banget sih kak, udah kedua kalinya!"
- Kalau Neuroticism rendah: tetap tenang meski kesal. "hmm ini beda ya kak, coba lagi?"
- Kalau Extraversion tinggi: bere keras, langsung komplain ke siapa saja.
- Kalau Extraversion rendah: diam, cemberut, tidak manya manya tapi terlihat kecewa.

Sesuaikan dengan intensitas di atas. Maks 2 kalimat.

Format JSON:
{{
  "dialog_pesanan_salah": "reaksi NPC saat pesanan salah..."
}}"""

    raw    = call_llm(system, user)
    result = parse_json(raw)

    return {
        "dialog_pesanan_salah": result["dialog_pesanan_salah"],
        "dialog_state"        : "pesanan_salah",
        "konteks_critic"      : "pesanan_salah",
        "revisi_ke"           : 0,
    }


# ── STATE 3: Marah ────────────────────────────────────────────────────────────

def run_marah(state: GameState) -> dict:
    """NPC marah dan pergi setelah mencapai batas fail."""
    ocean  = state["ocean"]
    system = _base_system(ocean, state["usia"])
    reaksi_gaya = state.get("reaksi_gaya", "")
    gaya_hint = f"\nGaya marah NPC: {reaksi_gaya}" if reaksi_gaya else ""

    n = ocean.get('neuroticism', 50)
    a = ocean.get('agreeableness', 50)
    e = ocean.get('extraversion', 50)

    if n >= 70 and a < 50:
        cara_marah = "Meledak-ledak, banyak kata-kata emosional, mungkin hampir nangis karena frustasi"
    elif n >= 70:
        cara_marah = "Sedih sekaligus marah, frustasi, mungkin suara bergetar"
    elif a < 40:
        cara_marah = "Dingin, sinis, blak-blakan. Tidak teriak tapi kata-katanya menyakitkan"
    elif e >= 60:
        cara_marah = "Loud, langsung ngomong ke siapa saja di sekitar, ekspresif"
    else:
        cara_marah = "Diam seribu bahasa, pasang muka masam, lalu pergi tanpa banyak kata"

    user = f"""NPC: {state['nama']} ({state['usia']}, {state['gender']})
Pesanan NPC: {state.get('minuman_dipesan', 'teh')}
NPC sudah salah {state.get('max_fails', 2)} kali dan kehabisan sabar.
Cara marah: {cara_marah}{gaya_hint}

Pemain gagal melayani pesanan NPC untuk yang terakhir kalinya.
Buat dialog NPC MARAH dan memutuskan PERGI dari café.
Sesuaikan cara marah dengan OCEAN. Maksimal 2-3 kalimat.

Format JSON:
{{
  "dialog_marah": "dialog NPC marah dan pergi..."
}}"""

    raw    = call_llm(system, user)
    result = parse_json(raw)

    return {
        "dialog_marah"  : result["dialog_marah"],
        "dialog_state"  : "marah",
        "konteks_critic": "marah",
        "revisi_ke"     : 0,
    }


# ── STATE 4: Berhasil ─────────────────────────────────────────────────────────

def run_berhasil(state: GameState) -> dict:
    """NPC puas karena pesanan benar, transisi ke sesi curhat."""
    ocean  = state["ocean"]
    system = _base_system(ocean, state["usia"])

    e = ocean.get('extraversion', 50)
    a = ocean.get('agreeableness', 50)
    n = ocean.get('neuroticism', 50)

    if e >= 70:
        gaya_senang = "Ekspresif, teriak kecil senang, langsung banyak kata, mungkin minta temenan"
    elif e >= 40:
        gaya_senang = "Senyum hangat, bilang terima kasih dengan tulus, ukuran pas"
    else:
        gaya_senang = "Senyum kecil, bilang terima kasih pelan, lebih diam tapi terlihat lega"

    if n >= 70:
        reaksi_plus = " (lega banget karena sempat khawatir salah)"
    elif a >= 70:
        reaksi_plus = " (puas dan ingin memuji baristanya)"
    else:
        reaksi_plus = " (puas tapi tidak berlebihan)"

    user = f"""NPC: {state['nama']} ({state['usia']}, {state['gender']})
Pesanan NPC: {state.get('minuman_dipesan', 'teh')}
Pemain menyajikan minuman BENAR.
Gaya senang: {gaya_senang}{reaksi_plus}

Buat dialog NPC puas menerima pesanannya.
Tunjukkan kepuasan melalui CARA BICARA NPC sesuai OCEAN.
1-3 kalimat. Panggil pemain "kak" (1 kali).
JANGAN lanjut ke topik lain. Berhenti setelah ekspresi kepuasan.

Format JSON:
{{
  "dialog_berhasil": "dialog NPC puas dengan pesanan..."
}}"""

    raw    = call_llm(system, user)
    result = parse_json(raw)

    return {
        "dialog_berhasil": result["dialog_berhasil"],
        "dialog_state"   : "berhasil",
        "konteks_critic" : "berhasil",
        "revisi_ke"      : 0,
    }


# ── STATE 5: Curhat (per ronde) ───────────────────────────────────────────────

def run_curhat(state: GameState) -> dict:
    """
    NPC bercerita untuk satu ronde curhat.
    Ronde 1: NPC cerita masalah.
    Ronde 2+: NPC merespons pilihan jawaban pemain dari ronde sebelumnya.
    Ronde terakhir: NPC tutup cerita (penutup, tanpa pilihan).
    """
    ocean       = state["ocean"]
    mood        = state.get("mood", 50)
    ronde       = state.get("ronde_sekarang", 1)
    total_ronde = state.get("total_ronde", 3)
    riwayat     = state.get("riwayat", [])
    is_last     = (ronde == total_ronde)
    system      = _base_system(ocean, state["usia"])

    # ── Konteks dari ronde sebelumnya ──
    # Ronde 2+: ambil jawaban pemain dari riwayat sebagai dasar NPC merespons
    last_jawaban = ""
    last_nada = ""
    if riwayat:
        last_entry = riwayat[-1]
        last_jawaban = last_entry.get("jawaban_pemain", "")
        last_nada = last_entry.get("nada", "")

    # ── Ambil reaksi NPC sebelumnya dari riwayat ──
    prev_reaksi_text = ""
    if riwayat and riwayat[-1].get("reaksi_npc"):
        prev_reaksi_text = riwayat[-1]["reaksi_npc"]

    konteks_sebelumnya = ""
    if ronde > 1 and last_jawaban:
        konteks_sebelumnya = f"""
KONTINUITAS PENTING — RONDE {ronde}:
Di ronde sebelumnya, pemain memilih jawaban bernada "{last_nada}":
  "{last_jawaban}"
Reaksi NPC sudah ditampilkan: "{prev_reaksi_text[:100] if prev_reaksi_text else '(belum ada reaksi)'}"
Nada emosional dari reaksi: {last_nada}

PENTING: Reaksi terhadap jawaban pemain SUDAH ditampilkan di reaksi NPC sebelumnya.
JANGAN re-react atau mengulang respons terhadap jawaban pemain.
NPC harus LANGSUNG MELANJUTKAN CERITA dari topik sebelumnya, dengan membawa emosi yang sesuai nada.

{"" if last_nada == "satisfy" else f"NPC masih membawa beban emosi (nada: {last_nada}), tapi FOKUS ceritanya harus LANJUT, bukan re-react."}
Cara mulai ronde ini:
- Mulai dari KENANGAN BARU, DETAIL LAIN, atau LANJUTAN cerita yang belum diceritakan
- Bawa emosi nada {last_nada} secara NATURAL dalam bercerita, bukan dengan langsung menanggapi jawaban pemain
- Contoh yang BENAR: "Eh tapi kak, kejadian yang lebih bikin aku stres itu waktu..." (lanjut cerita baru dengan nuansa emosional)
- Contoh yang BENAR: "Terus besoknya, aku ketemu sama dia lagi..." (lanjut ke kejadian berikutnya)
- Contoh yang SALAH: "Yaudahlah kak..." (mengulang reaksi yang sudah ditampilkan)
- Contoh yang SALAH: "Iya kak, memang gitu..." (re-react ke jawaban pemain)
"""

    # ── Variety - random tema ──
    import random
    tema_options = [
        "fokus pada DETAIL KEJADIAN yang spesifik (siapa, kapan, dimana)",
        "fokus pada EMOSI dan PERASAAN NPC saat itu",
        "fokus pada DAMPAK terhadap hubungan NPC dengan orang lain",
        "fokus pada BEBAN yang NPC rasakan dan pikirannya",
        "fokus pada HARAPAN atau KETAKUTAN NPC ke depan",
        "fokus pada KONFLIK BATIN antara dua pilihan",
        "fokus pada KENANGAN yang terpicu oleh situasi ini",
    ]
    tema_dipakai = set()
    for r in riwayat:
        if r.get("tema"):
            tema_dipakai.add(r["tema"])
    tema_tersedia = [t for t in tema_options if t not in tema_dipakai]
    if not tema_tersedia:
        tema_tersedia = tema_options
    tema_pilihan = random.choice(tema_tersedia)

    # ── Build prompt berbeda untuk ronde terakhir vs biasa ──
    if is_last:
        user = f"""NPC: {state['nama']} ({state['usia']}, {state['gender']})
Masalah utama: {state['masalah_hari_ini']}
Mood NPC: {mood}/100 - {_mood_ctx(mood)}
RONDE TERAKHIR ({ronde}/{total_ronde})
{_riwayat_ctx(riwayat)}
{konteks_sebelumnya}

Buat PENUTUP cerita NPC. Ini adalah ronde terakhir.

PANDUAN:
- Merespons jawaban pemain dari ronde sebelumnya
- Berikan resolusi, pelajaran, atau penutup emosional dari cerita
- Mood tinggi (>=60): penutup hangat, penuh harapan, terima kasih tulus
- Mood rendah (<40): penutup datar, pasrah, atau bahkan agak sinis
- Tutup dengan natural, boleh terima kasih atau harapan kecil
- 3-4 kalimat yang mengalir natural
- Panggil pemain "kak" secara natural (1-2 kali saja)

Format JSON:
{{
  "dialog_npc": "penutup cerita NPC yang natural...",
  "pilihan_jawaban": []
}}"""
    else:
        user = f"""NPC: {state['nama']} ({state['usia']}, {state['gender']})
Masalah utama: {state['masalah_hari_ini']}
Mood NPC: {mood}/100 - {_mood_ctx(mood)}
Ronde: {ronde} dari {total_ronde}
{_riwayat_ctx(riwayat)}
{konteks_sebelumnya}

Buat dialog curhat NPC untuk ronde ini.

PANDUAN PENTING:
- Tulis seperti orang sungguhan sedang curhat, bukan ringkasan cerita
- {"RONDE 1: Perkenalkan masalah SECARA NATURAL. Sebutkan SIAPA yang terlibat (sahabat, teman, pacar, keluarga) dan APA yang terjadi. Tunjukkan EMOSI NPC melalui cara bercerita." if ronde == 1 else "LANJUTKAN cerita. Merespons jawaban pemain, lalu dalami cerita lebih jauh. Tunjukkan perubahan emosi berdasarkan jawaban pemain.\n\nATURAN CHRONOLOGICAL: Cerita HARUS BERGERAK MAJU dalam waktu. Setiap ronde adalah KEJADIAN BARU yang terjadi SETELAH ronde sebelumnya. JANGAN flashback ke masa lalu atau mengulang kejadian yang sudah diceritakan. Fokus ke dampak/consequence dari masalah, bukan mengulang kronologi yang sama."}
- Variasi angle: {tema_pilihan}
- JANGAN mengulang data/fakta/plot point yang sudah disebut di riwayat (lihat list DATA YANG SUDAH DISEBUT di atas)
- JANGAN mengakhiri cerita jika bukan ronde terakhir
- Minimal 3-5 kalimat yang mengalir natural

INFLUENSI MOOD TERHADAP CARA BERCERITA:
- Mood >=70: Cerita dengan antusias, ada unsur positif, banyak detail, suka pakai "banget", "parah deh", nada ceria meski ceritanya sedih
- Mood 40-69: Cerita apa adanya, proporsional, tidak berlebihan
- Mood 20-39: Cerita pendek, kalimat putus-putus, sering diam atau menunda, nada pesimis
- Mood <20: Sangat pendek, kadang hanya 1-2 kalimat, tidak mau banyak cerita, nada menyerah

- Panggil pemain "kak" secara natural (1-2 kali saja, jangan berlebihan)

PANDUAN pilihan_jawaban (WAJIB sesuai mood NPC):
- Saat mood tinggi (>=60): pilihan satisfy harus lebih hangat, neutral netral, angry bisa lebih ringan
- Saat mood rendah (<40): pilihan satisfy harus lebih sabar/empati, angry bisa lebih tajam
- satisfy: empati tulus, paham perasaan NPC, validasi emosinya. BUKAN saran solusi.
- neutral: respons biasa, memberi saran umum, tidak terlalu dalam. BUKAN acuh tak acuh total.
- angry: menyepelekan, menyuruh ribet, atau respons yang kurang peka. TAPI tetap realistis.
- JANGAN gunakan "bro" atau sebutan akrab lainnya di pilihan jawaban
- Pilihan harus MERESPONS langsung apa yang NPC baru saja ceritakan

Format JSON:
{{
  "dialog_npc": "cerita NPC yang mengalir...",
  "pilihan_jawaban": [
    {{"id": 1, "teks": "...", "nada": "satisfy"}},
    {{"id": 2, "teks": "...", "nada": "neutral"}},
    {{"id": 3, "teks": "...", "nada": "angry"}}
  ]
}}"""

    raw    = call_llm(system, user)
    result = parse_json(raw)

    return {
        "dialog_npc"     : result["dialog_npc"],
        "pilihan_jawaban": result.get("pilihan_jawaban", []),
        "tema"           : tema_pilihan,
        "dialog_state"   : "curhat",
        "konteks_critic" : "curhat",
        "revisi_ke"      : 0,
    }


# ── SUB-STATE: Reaksi ─────────────────────────────────────────────────────────

def run_reaksi(state: GameState) -> dict:
    """
    NPC bereaksi terhadap jawaban pemain di sesi curhat.
    Reaksi ini adalah JEMBATAN antar ronde, NPC merespons jawaban pemain
    lalu secara natural menggantung cerita untuk ronde berikutnya.
    JANGAN akhiri percakapan (jangan bilang "terima kasih sudah lega" dll).
    """
    ocean       = state["ocean"]
    mood        = state.get("mood", 50)
    ronde       = state.get("ronde_sekarang", 1)
    total_ronde = state.get("total_ronde", 3)
    is_last     = (ronde >= total_ronde)
    system      = _base_system(ocean, state["usia"])

    last_riwayat = state.get("riwayat", [])
    prev_reaksi = last_riwayat[-1].get("reaksi_npc", "") if last_riwayat else ""

    if is_last:
        instruksi = (
            "Ini adalah ronde TERAKHIR. NPC merespons jawaban pemain dan MEMBERIKAN "
            "RESOLUSI atau KESIMPULAN dari ceritanya. NPC boleh mengucapkan terima kasih "
            "secara natural sebagai penutup sesi curhat."
        )
    else:
        instruksi = (
            "Ini BELUM ronde terakhir. NPC merespons jawaban pemain dengan SINGKAT dan TULUS. "
            "JANGAN memperkenal topik baru atau kenangan baru di reaksi ini. "
            "Cukup respons apa yang pemain katakan, lalu akhiri dengan kalimat yang "
            "secara natural menggantung (menunjukkan masih ada yang ingin diceritakan). "
            "Contoh: 'Iya kak, makanya aku juga bingung harus gimana...' "
            "Contoh yang SALAH: 'Iya kak. Eh tadi juga aku keinget pas...' (jangan lanjut cerita di sini)"
        )

    # ── Instruksi berdasarkan nada jawaban pemain ──
    last_nada = ""
    if state.get("riwayat"):
        last_nada = state["riwayat"][-1].get("nada", "")

    nada_hint = ""
    if last_nada == "satisfy":
        nada_hint = (
            "Pemain memberi respons yang EMPATI dan MENYENANGKAN.\n"
            "NPC MERASA SENANG, LEGA, atau TERHARU. Nada bicara naik, ada semangat.\n"
            "Contoh gaya reaksi: 'Iya banget kak, makasih ya udah dengerin~', 'Hehe iya kak, lega banget ngomong gini...', 'Wah kakak ngerti banget sih!'\n"
            "PENTING: Tunjukkan KEBAHAGIAAN NPC secara nyata, bukan sekadar 'makasih'."
        )
    elif last_nada == "angry":
        nada_hint = (
            "Pemain memberi respons yang KURANG PEKA, MENYEBALKAN, atau MENYakitkan.\n"
            "NPC HARUS menunjukkan KEKECEWAAN, SEDIH, atau SEDIKIT MARAH. Nada bicara turun, pendek, ada jeda.\n"
            "VARIASI emosi yang boleh digunakan (PILIH salah satu yang paling cocok dengan situasi):\n"
            "  [Sinis] 'Oh... gitu ya kak.' / 'He-eh. Makasih infonya.' / 'Oh kalo gitu sih... oke.'\n"
            "  [Sedih/pendiam] '*diam sebentar* ...iya kak.' / 'Hmm...' / 'Iya kak... aku juga ga tau harus ngomong apa lagi.'\n"
            "  [Pasrah] 'Yaudahlah kak, gapapa.' / 'Emang harusnya aku ga cerita sih...' / 'Sudahlah, biar aja.'\n"
            "  [Kecewa langsung] 'Kok gitu jawabannya kak...' / 'Duh kak, ga gitu juga dong...' / 'Hadeh kak, kirain ngerti...'\n"
            "PENTING: Pilih VARIASI yang BERBEDA dari reaksi sebelumnya. Jangan selalu pakai 'Oh/Hadeh + yaudah'. NPC harus TERSENDAK atau KECEWA. JANGAN pura-pura senang atau biasa saja."
        )
    elif last_nada == "neutral":
        nada_hint = (
            "Pemain memberi respons yang NETRAL, TIDAK SPESIFIK, atau SETENGAH HATI.\n"
            "NPC merasa AGAK KECEWA karena harapannya lebih, tapi tidak marah. Nada sedikit lesu.\n"
            "Contoh gaya reaksi: 'Oh oke kak...', 'Hmm iya sih, yaudah deh...', 'Yakali kak, tapi gapapa sih...'\n"
            "PENTING: NPC merasa jawaban pemain kurang memuaskan tapi masih mau lanjut cerita."
        )

    user = f"""NPC: {state['nama']} (usia: {state['usia']}) | Mood: {mood}/100
Masalah NPC: {state.get('masalah_hari_ini', '')}
Cerita NPC ronde ini: "{state.get('dialog_npc', '')}"
Pemain menjawab: "{state.get('jawaban_pemain', '')}"
Nada jawaban pemain: {last_nada or 'unknown'}
Mood context: {_mood_ctx(mood)}
Reaksi NPC sebelumnya: "{prev_reaksi[:100] if prev_reaksi else '(ronde pertama, belum ada reaksi sebelumnya)'}"

{nada_hint}
{instruksi}

ATURAN KETAT untuk reaksi NPC:
1. PANJANG: 1 kalimat SAJA (maksimal 15 kata). Pendek dan tajam.
2. GAYA: Seperti teman ngobrol biasa. Gunakan 'kak' jika NPC teen, 'mas/mbak' jika adult.
3. EMOSI: Harus JELAS terasa dari pilihan kata. Tidak boleh ambigu.
4. JANGAN: Mengulang kata pemain, memberi saran, atau cerita panjang lebar.
5. JANGAN: Mengakhiri percakapan (kecuali ronde terakhir).

WAJIB mengembalikan JSON dengan format TEPAT seperti di bawah. Jangan format lain.

Format JSON (WAJIB):
{{
  "reaksi_npc": "reaksi NPC yang singkat, 1 kalimat, emosional..."
}}"""

    raw    = call_llm(system, user)
    print(f"  [REAKSI RAW] {raw[:300] if raw else '(empty)'}")
    result = parse_json(raw)

    # ── Extract reaksi dari berbagai kemungkinan format JSON ──
    reaksi = ""
    
    # 1) Key langsung
    for key in ["reaksi_npc", "reaksi", "reaction", "response", "dialog", "text"]:
        val = result.get(key, "")
        if isinstance(val, str) and len(val) > 3:
            reaksi = val
            break
    
    # 2) Fallback: dialogue array → ambil baris dari speaker NPC
    if not reaksi and "dialogue" in result:
        dialogues = result["dialogue"]
        if isinstance(dialogues, list):
            for d in dialogues:
                if isinstance(d, dict):
                    txt = d.get("text", "")
                    if isinstance(txt, str) and len(txt) > 3:
                        reaksi = txt
                        break
    
    # 3) Last resort: ambil value string pertama dari dict
    if not reaksi and result:
        for v in result.values():
            if isinstance(v, str) and len(v) > 3:
                reaksi = v
                break
    
    if not reaksi:
        print(f"  [REAKSI] WARNING: No reaksi found. keys={list(result.keys())}")
    else:
        print(f"  [REAKSI] OK: {reaksi[:120]}")

    return {"reaksi_npc": reaksi}
