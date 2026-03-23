"""
Generate a Phase B Scouting Report for the worst performer in the latest season.
Fills out the prompt template from bt_evolution_plan.md Section 9.
"""

import json
import os
import glob
import anthropic

GEN_DIR = os.path.join(os.path.dirname(__file__), "..", "generations")

# Find latest generation
gen_dirs = sorted(glob.glob(os.path.join(GEN_DIR, "gen_*")))
if not gen_dirs:
    print("No generation directories found.")
    exit(1)
latest_gen = gen_dirs[-1]
print(f"Latest generation: {os.path.basename(latest_gen)}")

# Load generation summary to find worst performer
summary_path = os.path.join(latest_gen, "generation_summary.json")
with open(summary_path) as f:
    summary = json.load(f)

leaderboard = summary["leaderboard"]
worst = min(leaderboard, key=lambda x: x["elo"])
fighter_id = worst["fighter_id"]
fighter_name = worst["name"]
fighter_elo = worst["elo"]
print(f"Worst performer: {fighter_name} (Elo {fighter_elo:.1f})")

# Load behavior tree
fighter_dir = os.path.join(latest_gen, "fighters", f"{fighter_id}_{fighter_name}")
bt_path = os.path.join(fighter_dir, "bt_v0.json")
with open(bt_path) as f:
    bt_tree = json.load(f)

# Load status for aggregate metrics
status_path = os.path.join(fighter_dir, "status.json")
with open(status_path) as f:
    status = json.load(f)

# Load battle log summaries (exclude hit_log and tick_states to keep prompt manageable)
battles_dir = os.path.join(fighter_dir, "battles")
battle_files = sorted(glob.glob(os.path.join(battles_dir, "match_*.json")))

battle_summaries = []
for bf in battle_files:
    with open(bf) as f:
        match_data = json.load(f)
    # Strip verbose tick-level data, keep summary sections
    summary_entry = {
        "match_id": match_data["match_id"],
        "opponent": match_data["opponent"],
        "result": match_data["result"],
        "duration_ticks": match_data["duration_ticks"],
        "final_state": match_data["final_state"],
        "damage_summary": match_data["damage_summary"],
        "action_frequency": match_data["action_frequency"],
        "positional_summary": match_data["positional_summary"],
        "grapple_stats": match_data["grapple_stats"],
        "phase_breakdown": match_data["phase_breakdown"],
    }
    battle_summaries.append(summary_entry)

# Select a representative subset: 1 match per opponent (first match of each series)
opponents_seen = set()
representative_logs = []
for bs in battle_summaries:
    opp = bs["opponent"]
    if opp not in opponents_seen:
        opponents_seen.add(opp)
        representative_logs.append(bs)

# Also include a couple extra matches for variety (a win and a loss)
wins = [bs for bs in battle_summaries if bs["result"] == "win" and bs not in representative_logs]
losses = [bs for bs in battle_summaries if bs["result"] == "loss" and bs not in representative_logs]
if wins:
    representative_logs.append(wins[0])
if losses:
    representative_logs.append(losses[0])

logs_text = "\n---\n".join(json.dumps(log, indent=2) for log in representative_logs)

# Build the prompt from Section 9 of bt_evolution_plan.md
prompt = f"""You are analyzing a chain-fist fighting AI in a 2D arena tournament.

Fighters control two independent chain-fists that can be launched, locked mid-air
as pendulum anchors, and retracted to grapple-pull the body. Fist hits deal 12 damage.
Key tactics: ceiling grappling, pendulum swings, dive attacks, zoning with staggered
fists, and punishing overextended chains.

Below are the battle logs for contestant {fighter_id} "{fighter_name}" (Elo {fighter_elo:.1f}).
Record: {status['record']['wins']}W-{status['record']['losses']}L-{status['record']['draws']}D
Aggregate metrics:
- Avg damage dealt: {status['aggregate_metrics']['avg_damage_dealt']:.1f}
- Avg damage received: {status['aggregate_metrics']['avg_damage_received']:.1f}
- Avg hit accuracy: {status['aggregate_metrics']['avg_hit_accuracy']:.1%}
- Avg time grounded: {status['aggregate_metrics']['avg_time_grounded_pct']:.1%}
- Avg match duration: {status['aggregate_metrics']['avg_match_duration_ticks']:.0f} ticks

Its behavior tree structure is:
<tree>
{json.dumps(bt_tree, indent=2)}
</tree>

Battle log summaries ({len(representative_logs)} representative matches):
<logs>
{logs_text}
</logs>

Write a scouting report covering:
1. **Chain-fist management** — How effectively does it use both fists? Does it
   waste fists or leave itself vulnerable with both extending?
2. **Grapple/swing game** — Quality of anchor placement, swing timing, and
   aerial pressure. Does it exploit pendulum momentum well?
3. **Offensive patterns** — Hit rate, damage output, combo potential (e.g.
   anchor with one fist, strike with the other during swing).
4. **Defensive gaps** — When does it take damage? Does it get caught grounded
   with both fists busy? Does it fail to punish opponent whiffs?
5. **Matchup notes** — Which opponents/archetypes give it trouble and why.
6. **Suggested fix** — One concrete change to the tree that would address
   the single biggest weakness. Describe in plain English.

Keep the report under 300 words."""

print(f"\n{'='*60}")
print("FILLED PROMPT")
print(f"{'='*60}")
print(prompt[:500] + "...\n")
print(f"Prompt length: {len(prompt)} chars")
print(f"Battle logs included: {len(representative_logs)} matches")
print(f"{'='*60}\n")

# Call Claude API
print("Sending to Claude API...")
client = anthropic.Anthropic()
response = client.messages.create(
    model="claude-sonnet-4-20250514",
    max_tokens=1024,
    messages=[{"role": "user", "content": prompt}],
)

report = response.content[0].text

print(f"\n{'='*60}")
print(f"SCOUTING REPORT: {fighter_name} (Elo {fighter_elo:.1f})")
print(f"{'='*60}\n")
print(report)

# Save the report
report_path = os.path.join(fighter_dir, "scouting_report.md")
with open(report_path, "w") as f:
    f.write(f"# Scouting Report: {fighter_name}\n\n")
    f.write(f"**Elo:** {fighter_elo:.1f} | **Record:** {status['record']['wins']}W-{status['record']['losses']}L-{status['record']['draws']}D\n\n")
    f.write(report)

print(f"\nReport saved to: {report_path}")
