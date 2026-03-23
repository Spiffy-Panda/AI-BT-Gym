"""Analyze fist positions during the dead zone to check for overlap."""
import json

path = (
    "generations/gen_000/fighters/fighter_01_Green_GrappleAssassin/"
    "battles/match_001_g2_vs_fighter_00.json"
)
with open(path) as f:
    data = json.load(f)

checkpoints = data["replay"]["checkpoints"]

# Fist layout: [opp_right, opp_left, fig_right, fig_left]
# (need to verify from MatchRecorder)
print("=== FIST POSITIONS DURING DEAD ZONE (ticks 100-2300) ===")
print(f"{'tick':>5}  {'Opp.x':>7} {'Fig.x':>7} {'gap':>5}  |  "
      f"{'fist0_s':>3} {'fist0_x':>8}  {'fist1_s':>3} {'fist1_x':>8}  "
      f"{'fist2_s':>3} {'fist2_x':>8}  {'fist3_s':>3} {'fist3_x':>8}")

for cp in checkpoints:
    t = cp["t"]
    if t < 80 or t > 2350:
        continue
    if t > 120 and t < 2200 and t % 200 != 0:
        continue

    f0, f1 = cp["f"][0], cp["f"][1]
    fists = cp["fists"]
    gap = f0["x"] - f1["x"]

    fist_info = "  ".join(
        f"s{fi['s']} {fi['x']:8.1f}" for fi in fists
    )
    print(f"{t:5d}  {f0['x']:7.1f} {f1['x']:7.1f} {gap:5.1f}  |  {fist_info}")

print()
print("=== CHECKING FIST-VS-FIST OVERLAP ===")
overlaps = 0
for cp in checkpoints:
    t = cp["t"]
    if t < 83 or t > 2305:
        continue
    fists = cp["fists"]
    # Check all pairs of fists from different fighters
    # fists 0,1 = opponent; fists 2,3 = fighter (assumed)
    for i in [0, 1]:
        for j in [2, 3]:
            fi, fj = fists[i], fists[j]
            dx = abs(fi["x"] - fj["x"])
            dy = abs(fi["y"] - fj["y"])
            dist = (dx**2 + dy**2) ** 0.5
            if dist < 20:  # close enough to visually overlap
                overlaps += 1
                if overlaps <= 15:
                    print(f"  tick {t:5d}: fist{i}({fi['x']:7.1f},{fi['y']:6.1f} s{fi['s']}) vs "
                          f"fist{j}({fj['x']:7.1f},{fj['y']:6.1f} s{fj['s']}) dist={dist:.1f}")

print(f"\nTotal close-fist frames (dist<20): {overlaps} out of {len([c for c in checkpoints if 83 <= c['t'] <= 2305])} checkpoints")
