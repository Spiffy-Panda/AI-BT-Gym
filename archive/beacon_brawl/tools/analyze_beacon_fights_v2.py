import json, glob, os

base = "tournaments/beacon_brawl/gen_003/teams"

matchups = [
    ("team_04_Cyan_Turtle", "team_08", "Cyan_Turtle vs Pink_Rushdown"),
    ("team_07_White_Specialists", "team_00", "White_Specialists vs Red_Dominators"),
    ("team_04_Cyan_Turtle", "team_07", "Cyan_Turtle vs White_Specialists (1st vs 2nd)"),
]

for team_dir, opp_id, label in matchups:
    print(f"\n{'='*60}")
    print(f"  {label}")
    print(f"{'='*60}")
    
    pattern = os.path.join(base, team_dir, "battles", f"*vs_{opp_id}.json")
    files = sorted(glob.glob(pattern))
    
    for f in files[:3]:
        d = json.load(open(f))
        r = d["result"]
        sc = d["final_scores"]
        bc = d["beacon_control"]
        pos = d.get("positional_summary", {})
        acts = d.get("action_frequency", {})
        
        icon = {"win": "W", "loss": "L", "draw": "D"}[r]
        print(f"\n  [{icon}] Score: {sc[0]:2d}-{sc[1]:2d}  Duration: {d['duration_ticks']:4d}t ({d['duration_seconds']:.1f}s)")
        print(f"      Beacon: L={bc['time_owning_left_pct']*100:4.0f}%  C={bc['time_owning_center_pct']*100:4.0f}%  R={bc['time_owning_right_pct']*100:4.0f}%")
        print(f"      Captures: {bc['capture_count']}  Pos: ({pos.get('avg_x',0):.0f},{pos.get('avg_y',0):.0f})  Plat%: {pos.get('time_on_platform_pct',0)*100:.0f}%  Ground%: {pos.get('time_grounded_pct',0)*100:.0f}%")
        
        top_acts = sorted(acts.items(), key=lambda x: -x[1])[:5]
        act_str = "  ".join(f"{k}={v}" for k,v in top_acts)
        print(f"      Actions: {act_str}")
        
        for km in d.get("key_moments", [])[:3]:
            print(f"      @ {km['tick']:4d}t: {km['description']}")
