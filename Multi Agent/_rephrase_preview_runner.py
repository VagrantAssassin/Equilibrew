import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import experiment_critic_uncertainty as e

g = e.rephrase_seed(e.GOOD_SEED, e.DIALOGUE_MODEL, 0.7)
b = e.rephrase_seed(e.BAD_SEED, e.DIALOGUE_MODEL, 0.7)

out = {
    "good_orig": {k: e.GOOD_SEED[k] for k in e.REPHRASE_TEXT_FIELDS},
    "good_rephrase": {k: g[k] for k in e.REPHRASE_TEXT_FIELDS},
    "bad_orig": {k: e.BAD_SEED[k] for k in e.REPHRASE_TEXT_FIELDS},
    "bad_rephrase": {k: b[k] for k in e.REPHRASE_TEXT_FIELDS},
}
with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "_rephrase_preview.json"), "w", encoding="utf-8") as f:
    json.dump(out, f, ensure_ascii=False, indent=2)
print("DONE")