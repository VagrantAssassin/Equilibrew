import json

with open("d:/File Unity/Equilibrew/Multi Agent/pengujian_fix/output/critic_testing.json", "r", encoding="utf-8") as f:
    data = json.load(f)

# curhat entries
curhat_all = [c for c in data if c["dialog_state"] == "curhat"]
print(f"Total curhat entries: {len(curhat_all)}")

# Find the ones with LONGEST first-attempt critic text
# Sort by length of first catatan
curhat_sorted = sorted(
    curhat_all,
    key=lambda c: len(c["critic_log"][0]["catatan"][0]) if c["critic_log"] and c["critic_log"][0].get("catatan") else 0,
    reverse=True
)

print("\nTop 10 curhat with longest critic text:")
for i, c in enumerate(curhat_sorted[:10]):
    skors = [log["skor"] for log in c["critic_log"]]
    catatan_len = len(c["critic_log"][0]["catatan"][0]) if c["critic_log"] and c["critic_log"][0].get("catatan") else 0
    print(f"\n{i+1}. {c['nama']} ({c['jenis_profile']}) ke={c['ke']}")
    print(f"   attempts={len(c['critic_log'])} | skors={skors} | final={c['critic_skor']}")
    print(f"   catatan_len={catatan_len} chars")
    print(f"   catatan preview: {c['critic_log'][0]['catatan'][0][:200]}...")

# Also check: which have >1 attempt (initially failed)
curhat_multi = [c for c in curhat_all if len(c["critic_log"]) > 1]
print(f"\n\nCurhat with >1 attempt: {len(curhat_multi)}")
for c in curhat_multi:
    skors = [log["skor"] for log in c["critic_log"]]
    catatan_len = len(c["critic_log"][0]["catatan"][0]) if c["critic_log"] and c["critic_log"][0].get("catatan") else 0
    print(f"  {c['nama']} ({c['jenis_profile']}) ke={c['ke']} | attempts={len(c['critic_log'])} | skors={skors} | catatan_len={catatan_len}")