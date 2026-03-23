"""Analyze a fighter's battle data from the tournament server."""
import json, sys, urllib.request

base = "http://localhost:8585"
gen = int(sys.argv[1]) if len(sys.argv) > 1 else 0
fid = sys.argv[2] if len(sys.argv) > 2 else "fighter_04"

data = json.loads(urllib.request.urlopen(f"{base}/api/generations/{gen}/fighters/{fid}/battles").read())

wins = [b for b in data if b["result"] == "win"]
losses = [b for b in data if b["result"] == "loss"]
draws = [b for b in data if b["result"] == "draw"]

print(f"=== {data[0]['fighter']} Gen {gen} ===")
print(f"Record: {len(wins)}W-{len(losses)}L-{len(draws)}D ({len(data)} games)")

# Aggregate stats
total_dealt = sum(b["damage_summary"]["dealt"] for b in data)
total_recv = sum(b["damage_summary"]["received"] for b in data)
total_hits = sum(b["damage_summary"]["hits_landed"] for b in data)
total_launches = sum(b["damage_summary"]["launches_at_opponent"] for b in data)
avg_acc = total_hits / max(total_launches, 1)
avg_dist = sum(b["positional_summary"]["avg_distance_to_opponent"] for b in data) / len(data)
avg_near = sum(b["positional_summary"]["time_near_opponent_pct"] for b in data) / len(data)
avg_dur = sum(b["duration_ticks"] for b in data) / len(data)

print(f"Avg damage dealt/recv: {total_dealt/len(data):.1f} / {total_recv/len(data):.1f}")
print(f"Hit accuracy: {avg_acc:.1%} ({total_hits} hits / {total_launches} launches)")
print(f"Avg distance: {avg_dist:.0f} | Near opponent: {avg_near:.1%}")
print(f"Avg duration: {avg_dur:.0f} ticks")

# Action frequency totals
actions = {}
for b in data:
    for act, cnt in b["action_frequency"].items():
        actions[act] = actions.get(act, 0) + cnt
print(f"\nAction totals:")
for act, cnt in sorted(actions.items(), key=lambda x: -x[1]):
    print(f"  {act}: {cnt}")

# Grapple stats
total_attaches = sum(b["grapple_stats"]["attach_count"] for b in data)
ceiling = sum(b["grapple_stats"]["ceiling_attaches"] for b in data)
wall = sum(b["grapple_stats"]["wall_attaches"] for b in data)
print(f"\nGrapple: {total_attaches} attaches (ceiling: {ceiling}, wall: {wall})")

# Per-opponent breakdown
opponents = {}
for b in data:
    opp = b["opponent"]
    if opp not in opponents:
        opponents[opp] = {"w": 0, "l": 0, "d": 0, "dealt": 0, "recv": 0}
    r = b["result"]
    opponents[opp]["w" if r == "win" else "l" if r == "loss" else "d"] += 1
    opponents[opp]["dealt"] += b["damage_summary"]["dealt"]
    opponents[opp]["recv"] += b["damage_summary"]["received"]

print(f"\nPer-opponent:")
for opp, s in sorted(opponents.items(), key=lambda x: -(x[1]["w"])):
    games = s["w"] + s["l"] + s["d"]
    print(f"  {opp}: {s['w']}W-{s['l']}L-{s['d']}D  dmg {s['dealt']/games:.0f}/{s['recv']/games:.0f}")
