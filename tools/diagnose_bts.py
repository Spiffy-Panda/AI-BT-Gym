"""Diagnose BT issues by analyzing battle logs from latest beacon brawl generation."""
import json, os, glob, sys
from collections import defaultdict

base = "tournaments/beacon_brawl"
# Find latest gen
gens = sorted(glob.glob(f"{base}/gen_*"))
if not gens:
    print("No generations found")
    sys.exit(1)
gen_dir = gens[-1]
print(f"Analyzing {gen_dir}")

# Load all battle logs
fighter_dirs = sorted(glob.glob(f"{gen_dir}/teams/team_*")) or sorted(glob.glob(f"{gen_dir}/fighters/team_*"))

team_stats = {}
for fd in fighter_dirs:
    name = os.path.basename(fd).split("_", 2)[-1] if "_" in os.path.basename(fd) else os.path.basename(fd)
    team_id = os.path.basename(fd)[:7]  # e.g. "team_07"

    battles = []
    for bf in glob.glob(f"{fd}/battles/*.json"):
        with open(bf) as f:
            battles.append(json.load(f))

    if not battles:
        continue

    # Aggregate stats
    wins = sum(1 for b in battles if b.get("result") == "win")
    losses = sum(1 for b in battles if b.get("result") == "loss")
    draws = sum(1 for b in battles if b.get("result") == "draw")

    avg_score_self = sum((b.get("final_scores") or [0,0])[0] for b in battles) / len(battles)
    avg_score_opp = sum((b.get("final_scores") or [0,0])[1] for b in battles) / len(battles)

    # Beacon control
    avg_beacon_own = sum((b.get("beacon_control") or {}).get("total_beacon_ownership_pct", 0) for b in battles) / len(battles)
    avg_left = sum((b.get("beacon_control") or {}).get("time_owning_left_pct", 0) for b in battles) / len(battles)
    avg_center = sum((b.get("beacon_control") or {}).get("time_owning_center_pct", 0) for b in battles) / len(battles)
    avg_right = sum((b.get("beacon_control") or {}).get("time_owning_right_pct", 0) for b in battles) / len(battles)

    # Position
    avg_y = sum((b.get("positional_summary") or {}).get("avg_y", 0) for b in battles) / len(battles)
    avg_plat = sum((b.get("positional_summary") or {}).get("time_on_platform_pct", 0) for b in battles) / len(battles)
    avg_grounded = sum((b.get("positional_summary") or {}).get("time_grounded_pct", 0) for b in battles) / len(battles)

    # Action frequency aggregation
    action_totals = defaultdict(int)
    for b in battles:
        for act, cnt in (b.get("action_frequency") or {}).items():
            action_totals[act] += cnt
    top_actions = sorted(action_totals.items(), key=lambda x: -x[1])[:8]

    # Capture events
    avg_captures = sum((b.get("beacon_control") or {}).get("capture_count", 0) for b in battles) / len(battles)

    team_stats[name] = {
        "record": f"{wins}-{losses}-{draws}",
        "avg_score": f"{avg_score_self:.1f}-{avg_score_opp:.1f}",
        "beacon_own": f"{avg_beacon_own*100:.0f}%",
        "L/C/R": f"{avg_left*100:.0f}/{avg_center*100:.0f}/{avg_right*100:.0f}",
        "avg_y": f"{avg_y:.0f}",
        "plat%": f"{avg_plat*100:.0f}%",
        "ground%": f"{avg_grounded*100:.0f}%",
        "captures": f"{avg_captures:.1f}",
        "top_actions": top_actions,
    }

# Print sorted by win count
print(f"\n{'Team':<22} {'Record':<10} {'Score':<10} {'Beacons':<8} {'L/C/R':<12} {'AvgY':<6} {'Plat%':<6} {'Gnd%':<6} {'Caps':<5}")
print("-" * 95)
for name, s in sorted(team_stats.items(), key=lambda x: -int(x[1]["record"].split("-")[0])):
    print(f"{name:<22} {s['record']:<10} {s['avg_score']:<10} {s['beacon_own']:<8} {s['L/C/R']:<12} {s['avg_y']:<6} {s['plat%']:<6} {s['ground%']:<6} {s['captures']:<5}")

# Detailed action breakdown for bottom teams
print("\n\n=== Bottom Team Action Breakdowns ===")
bottom_teams = sorted(team_stats.items(), key=lambda x: int(x[1]["record"].split("-")[0]))[:4]
for name, s in bottom_teams:
    print(f"\n{name} ({s['record']}):")
    for act, cnt in s["top_actions"]:
        print(f"  {act:<35} {cnt:>6}")

# Look at specific matchups for worst teams
print("\n\n=== Worst Team Matchup Details ===")
for fd in fighter_dirs:
    name = os.path.basename(fd).split("_", 2)[-1] if "_" in os.path.basename(fd) else os.path.basename(fd)
    if name not in [n for n, _ in bottom_teams]:
        continue

    battles = []
    for bf in sorted(glob.glob(f"{fd}/battles/*.json")):
        with open(bf) as f:
            battles.append(json.load(f))

    print(f"\n{name}:")
    # Group by opponent
    by_opp = defaultdict(list)
    for b in battles:
        by_opp[b.get("opponent", "?")].append(b)

    for opp, opp_battles in sorted(by_opp.items()):
        results = [b["result"][0].upper() for b in opp_battles]
        scores = [(b.get("final_scores") or [0,0]) for b in opp_battles]
        score_str = ", ".join(f"{s[0]}-{s[1]}" for s in scores)
        print(f"  vs {opp:<22} {''.join(results):<6} scores: {score_str}")
