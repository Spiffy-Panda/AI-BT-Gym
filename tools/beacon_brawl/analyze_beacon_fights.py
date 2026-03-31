import json, glob, os, sys

base = "tournaments/beacon_brawl/gen_002/teams"

# Analyze selected matchups
matchups = [
    ("team_04_Cyan_Turtle", "team_08", "Cyan_Turtle vs Pink_Rushdown (1st vs last)"),
    ("team_04_Cyan_Turtle", "team_09", "Cyan_Turtle vs Lime_Adaptive (1st vs 2nd)"),
    ("team_00_Red_Dominators", "team_01", "Red_Dominators vs Blue_Splitters (3rd vs 4th)"),
    ("team_03_Yellow_Bullies", "team_02", "Yellow_Bullies vs Green_Rotators (5th vs 9th)"),
    ("team_05_Magenta_Swingers", "team_07", "Magenta_Swingers vs White_Specialists (6th vs 7th)"),
    ("team_09_Lime_Adaptive", "team_08", "Lime_Adaptive vs Pink_Rushdown (2nd vs last)"),
    ("team_00_Red_Dominators", "team_08", "Red_Dominators vs Pink_Rushdown (3rd vs last)"),
    ("team_06_Orange_Opportunists", "team_02", "Orange_Opportunists vs Green_Rotators (8th vs 9th)"),
]

for team_dir, opp_id, label in matchups:
    print(f"\n{'='*70}")
    print(f"  {label}")
    print(f"{'='*70}")
    
    pattern = os.path.join(base, team_dir, "battles", f"*vs_{opp_id}.json")
    files = sorted(glob.glob(pattern))
    
    wins = losses = draws = 0
    total_score_my = total_score_opp = 0
    
    for f in files:
        d = json.load(open(f))
        r = d["result"]
        sc = d["final_scores"]
        bc = d["beacon_control"]
        dur = d["duration_ticks"]
        acts = d.get("action_frequency", {})
        pos = d.get("positional_summary", {})
        pulse = d.get("pulse_stats", {})
        
        if r == "win": wins += 1
        elif r == "loss": losses += 1
        else: draws += 1
        
        total_score_my += sc[0]
        total_score_opp += sc[1]
        
        result_icon = {"win": "W", "loss": "L", "draw": "D"}[r]
        print(f"\n  [{result_icon}] Score: {sc[0]:2d}-{sc[1]:2d}  Duration: {dur:4d}t ({dur/60:.1f}s)")
        print(f"      Beacon own: L={bc['time_owning_left_pct']*100:4.0f}%  C={bc['time_owning_center_pct']*100:4.0f}%  R={bc['time_owning_right_pct']*100:4.0f}%  (total={bc['total_beacon_ownership_pct']*100:.0f}%)")
        print(f"      Captures: {bc['capture_count']}  Lost: {bc['lost_count']}")
        
        top_acts = sorted(acts.items(), key=lambda x: -x[1])[:6]
        act_str = "  ".join(f"{k}={v}" for k,v in top_acts)
        print(f"      Actions: {act_str}")
        
        print(f"      Avg pos: ({pos.get('avg_x',0):.0f}, {pos.get('avg_y',0):.0f})  Ground%: {pos.get('time_grounded_pct',0)*100:.0f}%  Platform%: {pos.get('time_on_platform_pct',0)*100:.0f}%")
        print(f"      Pulses: {pulse.get('pulse_count',0)}  Stunned: {pulse.get('times_stunned',0)}")
        
        # Key moments
        for km in d.get("key_moments", [])[:3]:
            print(f"      @ {km['tick']:4d}t: {km['description']}")
    
    n = len(files)
    print(f"\n  Series: {wins}W-{losses}L-{draws}D  Avg Score: {total_score_my/n:.1f}-{total_score_opp/n:.1f}")

