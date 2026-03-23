"""Parse a fighter's status.json and print a summary."""
import json, sys

path = sys.argv[1]
with open(path) as f:
    d = json.load(f)

m = d["aggregate_metrics"]
r = d["record"]
print(f"Record: {r['wins']}-{r['losses']}-{r['draws']}")
print(f"Elo: {d['elo']:.1f}")
print(f"WR: {d['win_rate']*100:.1f}%")
print(f"Accuracy: {m['avg_hit_accuracy']*100:.1f}%")
print(f"Dmg dealt: {m['avg_damage_dealt']:.1f}")
print(f"Dmg recv: {m['avg_damage_received']:.1f}")
print(f"KOs: {m['total_knockouts']}")
print(f"Grounded: {m['avg_time_grounded_pct']*100:.1f}%")
print("---Matchups---")
results = {}
for mr in d["matchup_results"]:
    results.setdefault(mr["opponent"], []).append(mr["result"])
for opp, rs in results.items():
    name = opp.split("_", 2)[2]
    w = sum(1 for r in rs if r == "win")
    l = sum(1 for r in rs if r == "loss")
    dr = sum(1 for r in rs if r == "draw")
    print(f"  vs {name}: {w}-{l}-{dr}")
