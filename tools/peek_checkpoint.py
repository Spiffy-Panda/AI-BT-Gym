"""Quick peek at checkpoint structure."""
import json

path = (
    "generations/gen_000/fighters/fighter_01_Green_GrappleAssassin/"
    "battles/match_001_g2_vs_fighter_00.json"
)
with open(path) as f:
    data = json.load(f)

cp = data["replay"]["checkpoints"][0]
print("Checkpoint keys:", list(cp.keys()))
print("First checkpoint sample:", json.dumps(cp, indent=2)[:500])
