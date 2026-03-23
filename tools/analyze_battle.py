"""Analyze a battle JSON for softlocks / dead zones."""
import json, sys

path = sys.argv[1] if len(sys.argv) > 1 else (
    "generations/gen_000/fighters/fighter_01_Green_GrappleAssassin/"
    "battles/match_001_g2_vs_fighter_00.json"
)

with open(path) as f:
    data = json.load(f)

print(f"=== {data['fighter']} vs {data['opponent']} ===")
print(f"Result: {data['result']}  Duration: {data['duration_ticks']} ticks ({data['duration_seconds']:.1f}s)")
print(f"Damage dealt: {data['damage_summary']['dealt']}  received: {data['damage_summary']['received']}")
print(f"Hits landed: {data['damage_summary']['hits_landed']}  taken: {data['damage_summary']['hits_taken']}")
print(f"Launches at opponent: {data['damage_summary']['launches_at_opponent']}  accuracy: {data['damage_summary']['hit_accuracy']:.4f}")
print()

# === Key Moments ===
print("=== KEY MOMENTS ===")
for m in data["key_moments"]:
    print(f"  tick {m['tick']:5d}: {m['event']} — {m['description']}")
print()

# === Hit Log Gap Analysis ===
print("=== HIT LOG (gap analysis) ===")
hits = data["hit_log"]
prev_tick = 0
for h in hits:
    gap = h["tick"] - prev_tick
    marker = " <<<< DEAD ZONE" if gap > 200 else ""
    print(f"  tick {h['tick']:5d} ({h['attacker']:8s} {h['hand']:5s} {h['damage']:2d}dmg)  gap={gap:5d}{marker}")
    prev_tick = h["tick"]
print()

# === Phase Breakdown ===
print("=== PHASE BREAKDOWN ===")
for p in data["phase_breakdown"]:
    print(f"  {p['phase']:5s} ticks {p['tick_range'][0]:5d}-{p['tick_range'][1]:5d}  "
          f"dealt={p['damage_dealt']:3d} recv={p['damage_received']:3d}  "
          f"hp={p['hp_at_end']}  actions={p['dominant_actions']}")
print()

# === Replay Checkpoint Sampling ===
print("=== REPLAY CHECKPOINTS (sampled) ===")
replay = data["replay"]
checkpoints = replay["checkpoints"]
print(f"Total checkpoints: {len(checkpoints)}, interval: {replay['checkpoint_interval']}")
print()

# Sample evenly + around the dead zone boundaries
sample_count = min(25, len(checkpoints))
step = max(1, len(checkpoints) // sample_count)
for i in range(0, len(checkpoints), step):
    cp = checkpoints[i]
    t = cp["t"]
    fs = cp["f"]
    f0, f1 = fs[0], fs[1]
    dist = ((f0["x"] - f1["x"])**2 + (f0["y"] - f1["y"])**2) ** 0.5
    fists = cp.get("fists", [])
    # fist state: 0=idle, 1=flying, 2=attached, 3=retracting (check Fist.cs for enum)
    fist_states = " ".join(f"s{fi['s']}" for fi in fists)
    print(f"  tick {t:5d}: "
          f"Opp({f0['x']:7.1f},{f0['y']:6.1f} hp={f0['hp']:3d} vx={f0['vx']:7.1f} g={'Y' if f0['g'] else 'N'}) "
          f"Fig({f1['x']:7.1f},{f1['y']:6.1f} hp={f1['hp']:3d} vx={f1['vx']:7.1f} g={'Y' if f1['g'] else 'N'}) "
          f"dist={dist:6.1f} fists=[{fist_states}]")
print()

# === Action Frequency ===
print("=== ACTION FREQUENCY ===")
for action, count in sorted(data["action_frequency"].items(), key=lambda x: -x[1]):
    print(f"  {action:30s} {count:5d}")
print()

# === Grapple Stats ===
print("=== GRAPPLE STATS ===")
gs = data["grapple_stats"]
for k, v in gs.items():
    print(f"  {k}: {v}")
