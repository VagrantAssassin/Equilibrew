import json

with open("d:/File Unity/Equilibrew/Multi Agent/pengujian_fix/output/critic_testing.json", "r", encoding="utf-8") as f:
    data = json.load(f)

pesanan_multi = [c for c in data if c["dialog_state"] == "pesanan" and len(c["critic_log"]) > 1]
print(f"Total pesanan with >1 attempt: {len(pesanan_multi)}")
for c in pesanan_multi:
    skors = [log["skor"] for log in c["critic_log"]]
    print(f"  {c['nama']} ({c['jenis_profile']}) ke={c['ke']} | attempts={len(c['critic_log'])} | skors={skors} | final={c['critic_skor']}")

# Also show the top 5 lowest-score pesanan (all)
pesanan_all = [c for c in data if c["dialog_state"] == "pesanan"]
pesanan_sorted = sorted(pesanan_all, key=lambda c: c["critic_skor"])
print(f"\nTotal pesanan: {len(pesanan_all)}")
print("Top 5 lowest score:")
for c in pesanan_sorted[:5]:
    skors = [log["skor"] for log in c["critic_log"]]
    print(f"  {c['nama']} ({c['jenis_profile']}) ke={c['ke']} | attempts={len(c['critic_log'])} | skors={skors} | final={c['critic_skor']}")