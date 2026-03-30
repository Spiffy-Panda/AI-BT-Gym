import json, glob, os
from collections import Counter

base = "tournaments/beacon_brawl/gen_003/teams"
teams = sorted(os.listdir(base))

# Per-team summaries
print("=== TEAM SUMMARIES ===")
for t in teams:
    status = json.load(open(os.path.join(base, t, "status.json")))
    m = status["aggregate_metrics"]
    r = status["record"]
    print(f"\n{status['name']:25s}  {r['wins']:2d}W-{r['losses']:2d}L-{r['draws']:2d}D  Elo: {status['elo']:.0f}")
    print(f"  Avg Duration: {m['avg_match_duration_ticks']:.0f}t ({m['avg_match_duration_ticks']/60:.1f}s)  Score Diff: {m['avg_score_diff']:+.1f}")
    print(f"  Beacon Own%: {m['avg_beacon_ownership_pct']*100:.1f}%  Pulses: {m['avg_pulse_count']:.1f}")
    print(f"  Score Victories: {m['total_score_victories']}  Timeout Wins: {m['total_timeout_wins']}")

# Overall results
print("\n\n=== OVERALL STATISTICS ===")
results = Counter()
score_patterns = Counter()
center_caps = 0
total_games = 0
all_center_own = []
all_platform_pct = []

for t in teams:
    battles_dir = os.path.join(base, t, "battles")
    for f in sorted(glob.glob(os.path.join(battles_dir, "*.json"))):
        d = json.load(open(f))
        results[d["result"]] += 1
        sc = tuple(d["final_scores"])
        score_patterns[sc] += 1
        bc = d["beacon_control"]
        all_center_own.append(bc["time_owning_center_pct"])
        all_platform_pct.append(d.get("positional_summary", {}).get("time_on_platform_pct", 0))
        if bc["time_owning_center_pct"] > 0.1:
            center_caps += 1
        total_games += 1

for r, c in results.most_common():
    print(f"  {r}: {c} ({c/total_games*100:.1f}%)")

print(f"\n=== CENTER BEACON USAGE ===")
print(f"  Games with >10% center ownership: {center_caps}/{total_games} ({center_caps/total_games*100:.1f}%)")
avg_center = sum(all_center_own) / len(all_center_own) if all_center_own else 0
print(f"  Average center ownership: {avg_center*100:.1f}%")
avg_plat = sum(all_platform_pct) / len(all_platform_pct) if all_platform_pct else 0
print(f"  Average platform time: {avg_plat*100:.1f}%")

print(f"\n=== SCORE PATTERNS (top 10) ===")
for sc, c in score_patterns.most_common(10):
    print(f"  {sc[0]:2d}-{sc[1]:2d}: {c:4d} times")

# Check Pink_Rushdown positioning specifically
print(f"\n=== PINK_RUSHDOWN POSITIONING CHECK ===")
pink_dir = os.path.join(base, "team_08_Pink_Rushdown", "battles")
for f in sorted(glob.glob(os.path.join(pink_dir, "*.json")))[:3]:
    d = json.load(open(f))
    pos = d.get("positional_summary", {})
    bc = d["beacon_control"]
    print(f"  vs {d['opponent']}: score {d['final_scores'][0]}-{d['final_scores'][1]}  avg_y={pos.get('avg_y',0):.0f}  platform%={pos.get('time_on_platform_pct',0)*100:.0f}%  center_own={bc['time_owning_center_pct']*100:.0f}%")
