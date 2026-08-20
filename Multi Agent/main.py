"""
main.py — Entry point sistem Multi-Agent Tea'n Brew.
"""

from graph  import app, jalankan_dialog_state, update_mood, random_total_ronde
from config import MOOD_AWAL
from agents.dialogue_agent import (
    run_pesanan_salah, run_marah, run_berhasil,
    run_curhat, run_reaksi, run_closing
)

SEP = "=" * 64

def mood_bar(mood: int) -> str:
    return "█" * (mood // 10) + "░" * (10 - mood // 10)

def mood_label(mood: int) -> str:
    if mood >= 70: return "😊 Senang"
    if mood >= 40: return "😐 Netral"
    return "😠 Kesal"

def ph(title: str):
    print(f"\n{SEP}\n  {title}\n{SEP}")

def ps(title: str):
    pad = (62 - len(title) - 2) // 2
    print(f"\n  {'─'*pad} {title} {'─'*pad}")

def print_critic(state: dict, label: str = ""):
    skor    = state.get("critic_skor", 0)
    lulus   = state.get("critic_lulus", False)
    icon    = "✅" if lulus else "⚠️ "
    catatan = state.get("critic_catatan", [])
    info    = f" | {catatan[0]}" if catatan else ""
    tag     = f" [{label}]" if label else ""
    print(f"  {icon} Critic{tag}: {skor:.1f}/5.0{info}")

def cetak_dialog_npc(nama: str, teks: str):
    """
    Tampilkan dialog NPC dengan format yang rapi dan mudah dibaca.
    Setiap baris baru dalam dialog ditampilkan dengan indentasi konsisten.
    """
    print(f"\n  ┌─ {nama} ─────────────────────────────────────────")
    # Pecah berdasarkan newline atau titik akhir kalimat
    baris_list = [b.strip() for b in teks.replace("\\n", "\n").split("\n") if b.strip()]
    if len(baris_list) <= 1:
        # Tidak ada newline — pecah per kalimat agar lebih mudah dibaca
        import re
        baris_list = [b.strip() for b in re.split(r'(?<=[.!?])\s+', teks) if b.strip()]
    for baris in baris_list:
        # Wrap baris yang terlalu panjang
        while len(baris) > 56:
            potong = baris[:56].rfind(" ")
            potong = potong if potong > 30 else 56
            print(f"  │  {baris[:potong]}")
            baris = baris[potong:].strip()
        if baris:
            print(f"  │  {baris}")
    print(f"  └{'─'*57}")


def tampilkan_profil(s: dict):
    ps("PROFIL NPC")
    print(f"  Nama      : {s['nama']}")
    print(f"  Usia      : {s['usia']} ({s['gender']})")
    print(f"  Background: {s['background']}")
    print(f"  Masalah   : {s['masalah_hari_ini']}")
    print(f"  Max fails : {s['max_fails']} kali")
    print(f"\n  Skor OCEAN:")
    for k, v in s["ocean"].items():
        bar = "█" * (v // 10) + "░" * (10 - v // 10)
        print(f"    {k.capitalize():<20}: {bar} {v:>3}/100")


def tampilkan_pesanan(s: dict):
    ps("SESI PESANAN")
    cetak_dialog_npc(s["nama"], s["dialog_pesanan"])
    print(f"  (Memesan: {s['minuman_dipesan']})")
    print_critic(s, "pesanan")


def tampilkan_pesanan_salah(s: dict):
    cetak_dialog_npc(s["nama"], s.get("dialog_pesanan_salah", "-"))
    print_critic(s, "pesanan_salah")
    print(f"  ⚠️  Kesalahan ke-{s.get('fail_count', 1)} / {s.get('max_fails', 2)}")


def tampilkan_marah(s: dict):
    ps("PELANGGAN MARAH — PERGI")
    cetak_dialog_npc(s["nama"], s.get("dialog_marah", "-"))
    print_critic(s, "marah")
    print(f"\n  💢 Score -10 (penalty)")


def tampilkan_berhasil(s: dict):
    ps("PESANAN BENAR — LANJUT KE CURHAT")
    cetak_dialog_npc(s["nama"], s.get("dialog_berhasil", "-"))
    print_critic(s, "berhasil")


def tampilkan_curhat_dan_input(s: dict) -> str:
    ronde   = s.get("ronde_sekarang", 1)
    total   = s.get("total_ronde", 3)
    mood    = s.get("mood", 50)
    pilihan = s.get("pilihan_jawaban", [])

    print(f"\n{SEP}")
    print(f"  RONDE {ronde}/{total}  |  Mood: [{mood_bar(mood)}] {mood}/100 {mood_label(mood)}")
    print(SEP)

    cetak_dialog_npc(s["nama"], s.get("dialog_npc", "-"))
    print_critic(s, f"curhat ronde {ronde}")

    print(f"\n  Pilihan jawaban pemain:")
    nada_icon = {"satisfy": "💚", "neutral": "💛", "angry": "🔴"}
    mood_efek = {"satisfy": "+10", "neutral": " +0", "angry": "-10"}
    for p in pilihan:
        print(f"  [{p['id']}] {nada_icon.get(p['nada'], '•')} "
              f"(mood {mood_efek.get(p['nada'], '  0')})  {p['teks']}")
    print(f"  [0] Ketik jawaban sendiri (dinilai otomatis oleh AI)")

    while True:
        pilih = input(f"\n  Pilih (0/1/2/3): ").strip()
        if pilih == "0":
            return input("  Ketik jawabanmu: ").strip() or "(diam)"
        elif pilih in ["1", "2", "3"]:
            idx = int(pilih) - 1
            if idx < len(pilihan):
                return pilihan[idx]["teks"]
        print("  Input tidak valid, coba lagi.")


def tampilkan_reaksi(s: dict, jawaban: str, mood_lama: int):
    mood_baru = s.get("mood", mood_lama)
    delta     = mood_baru - mood_lama
    delta_str = f"+{delta}" if delta > 0 else str(delta)
    icon      = "⬆️" if delta > 0 else ("⬇️" if delta < 0 else "➡️")
    riwayat   = s.get("riwayat", [])
    nada      = riwayat[-1]["nada"] if riwayat else "neutral"
    nada_icon = {"satisfy": "💚", "neutral": "💛", "angry": "🔴"}

    print(f"\n  [PEMAIN]: \"{jawaban}\"")
    print(f"  Dinilai : {nada_icon.get(nada, '')} {nada}")
    cetak_dialog_npc(s["nama"], s.get("reaksi_npc", "-"))
    print(f"\n  {icon} Mood: {mood_lama} {delta_str} → {mood_baru}/100 {mood_label(mood_baru)}")


def tampilkan_closing(s: dict):
    """Tampilkan dialog penutup sesi curhat dari NPC."""
    ps("PENUTUP SESI")
    cetak_dialog_npc(s["nama"], s.get("dialog_closing", "-"))


def tampilkan_ringkasan(s: dict):
    ph("SESI SELESAI — RINGKASAN")
    print(f"  NPC        : {s['nama']} ({s['usia']}, {s['gender']})")
    print(f"  Mood awal  : {MOOD_AWAL}/100")
    print(f"  Mood akhir : {s.get('mood', MOOD_AWAL)}/100 {mood_label(s.get('mood', MOOD_AWAL))}")
    print(f"\n  Riwayat percakapan:")
    nada_icon = {"satisfy": "💚", "neutral": "💛", "angry": "🔴"}
    for r in s.get("riwayat", []):
        delta     = r["mood_sesudah"] - r["mood_sebelum"]
        delta_str = f"+{delta}" if delta > 0 else str(delta)
        print(f"  Ronde {r['ronde']}: {nada_icon.get(r['nada'], '•')} "
              f"{r['nada']:<8} (mood {delta_str:>3}) → \"{r['jawaban_pemain'][:45]}\"")
    print(f"\n{SEP}\n")


def run_session(usia: str, gender: str, mood_awal: int = MOOD_AWAL):
    ph("MULTI-AGENT DIALOG SYSTEM — Tea'n Brew")

    current = {"usia": usia, "gender": gender, "mood": mood_awal}

    for step in app.stream(current, stream_mode="updates"):
        node_name = list(step.keys())[0]
        updates   = step[node_name]
        if updates is None:
            continue
        current.update(updates)

        if node_name == "profile_agent":
            tampilkan_profil(current)
        elif node_name == "critic_agent":
            icon = "✅" if current.get("critic_lulus") else "❌"
            skor = current.get("critic_skor", 0)
            print(f"\n  [CRITIC — {current.get('konteks_critic', '')}] {icon} {skor:.1f}/5.0", end="")
            if not current.get("critic_lulus") and current.get("revisi_ke", 0) <= 2:
                print(f" → revisi ke-{current['revisi_ke']}...", end="")
            print()

    tampilkan_pesanan(current)

    ps("SESI ORDERING")
    print(f"  Simulasi pemain membuat: {current.get('minuman_dipesan', '?')}")
    print(f"  (Unity: input dari tombol Serve)")

    selesai_ordering = False
    while not selesai_ordering:
        jawab = input("\n  Apakah pesanan benar? (y/n): ").strip().lower()

        if jawab == "y":
            print(f"\n  [AGENT 2] Dialogue Agent → dialog berhasil...")
            current = jalankan_dialog_state(current, run_berhasil)
            tampilkan_berhasil(current)
            selesai_ordering = True
        else:
            current["fail_count"] = current.get("fail_count", 0) + 1
            if current["fail_count"] >= current.get("max_fails", 2):
                print(f"\n  [AGENT 2] Dialogue Agent → dialog marah...")
                current = jalankan_dialog_state(current, run_marah)
                tampilkan_marah(current)
                tampilkan_ringkasan(current)
                return
            else:
                print(f"\n  [AGENT 2] Dialogue Agent → dialog pesanan salah...")
                current = jalankan_dialog_state(current, run_pesanan_salah)
                tampilkan_pesanan_salah(current)

    total_ronde = random_total_ronde()
    current["total_ronde"]    = total_ronde
    current["ronde_sekarang"] = 1

    ps("SESI CURHAT")
    print(f"  Total ronde: {total_ronde}")

    for ronde in range(1, total_ronde + 1):
        current["ronde_sekarang"] = ronde

        print(f"\n  [AGENT 2] Dialogue Agent → curhat ronde {ronde}...")
        current = jalankan_dialog_state(current, run_curhat)

        mood_sebelum              = current.get("mood", mood_awal)
        jawaban                   = tampilkan_curhat_dan_input(current)
        current["jawaban_pemain"] = jawaban

        mood_update = update_mood(current)
        current.update(mood_update)

        reaksi = run_reaksi(current)
        current.update(reaksi)

        tampilkan_reaksi(current, jawaban, mood_sebelum)

    # ── Setelah semua ronde selesai, tampilkan penutup ──
    print(f"\n  [AGENT 2] Dialogue Agent → penutup sesi curhat...")
    closing = run_closing(current)
    current.update(closing)
    tampilkan_closing(current)

    tampilkan_ringkasan(current)


if __name__ == "__main__":
    ph("Tea'n Brew — Multi-Agent Dialog System (LangGraph)")

    usia_map = {"1": "remaja", "2": "dewasa", "3": "orang tua"}
    print("\nKategori usia NPC:")
    for k, v in usia_map.items():
        print(f"  [{k}] {v}")
    usia = usia_map.get(input("Pilih usia (1-3): ").strip(), "dewasa")

    print("\nGender NPC:")
    print("  [1] pria\n  [2] wanita")
    gender = "pria" if input("Pilih gender (1-2): ").strip() == "1" else "wanita"

    run_session(usia, gender)
