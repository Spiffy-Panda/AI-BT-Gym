# Scouting Report Prompt — Yellow_DiveKicker (fighter_04)
## Gen 0 | Elo 747.6 | Record 5W-30L-0D

---

You are analyzing a chain-fist fighting AI in a 2D arena tournament.

Fighters control two independent chain-fists that can be launched, locked mid-air
as pendulum anchors, and retracted to grapple-pull the body. Fist hits deal 12 damage.
Key tactics: ceiling grappling, pendulum swings, dive attacks, zoning with staggered
fists, and punishing overextended chains.

Below are the battle logs for contestant fighter_04 "Yellow_DiveKicker" (Elo 747.6).
Record: 5W-30L-0D
Aggregate metrics:
- Avg damage dealt: 50.4
- Avg damage received: 101.8
- Avg hit accuracy: 20.1%
- Avg time grounded: 2.4%
- Avg match duration: 313 ticks

Its behavior tree structure is:
<tree>
[
  {
    "type": "Selector",
    "children": [
      {
        "type": "Sequence",
        "children": [
          {"type": "Condition", "value": "left_attached == 1"},
          {"type": "Condition", "value": "left_state == 2"},
          {"type": "Action", "value": "retract_left"}
        ]
      },
      {
        "type": "Sequence",
        "children": [
          {"type": "Condition", "value": "right_attached == 1"},
          {"type": "Condition", "value": "right_state == 2"},
          {"type": "Action", "value": "retract_right"}
        ]
      },
      {
        "type": "Sequence",
        "children": [
          {"type": "Condition", "value": "is_grounded == 0"},
          {"type": "Condition", "value": "vel_y > 0"},
          {"type": "Condition", "value": "distance_to_opponent < 230"},
          {
            "type": "Selector",
            "children": [
              {
                "type": "Sequence",
                "children": [
                  {"type": "Condition", "value": "left_retracted == 1"},
                  {"type": "Action", "value": "launch_left_at_opponent"}
                ]
              },
              {
                "type": "Sequence",
                "children": [
                  {"type": "Condition", "value": "right_retracted == 1"},
                  {"type": "Action", "value": "launch_right_at_opponent"}
                ]
              }
            ]
          }
        ]
      },
      {
        "type": "Sequence",
        "children": [
          {"type": "Condition", "value": "is_grounded == 0"},
          {"type": "Condition", "value": "vel_y < -30"},
          {"type": "Condition", "value": "distance_to_opponent < 200"},
          {
            "type": "Selector",
            "children": [
              {
                "type": "Sequence",
                "children": [
                  {"type": "Condition", "value": "left_retracted == 1"},
                  {"type": "Action", "value": "launch_left_down"}
                ]
              },
              {
                "type": "Sequence",
                "children": [
                  {"type": "Condition", "value": "right_retracted == 1"},
                  {"type": "Action", "value": "launch_right_down"}
                ]
              }
            ]
          }
        ]
      },
      {
        "type": "Sequence",
        "children": [
          {"type": "Condition", "value": "distance_to_opponent > 250"},
          {"type": "Condition", "value": "left_state == 1"},
          {"type": "Condition", "value": "left_chain > 80"},
          {"type": "Action", "value": "lock_left"}
        ]
      },
      {
        "type": "Sequence",
        "children": [
          {"type": "Condition", "value": "distance_to_opponent > 250"},
          {"type": "Condition", "value": "right_state == 1"},
          {"type": "Condition", "value": "right_chain > 80"},
          {"type": "Action", "value": "lock_right"}
        ]
      },
      {
        "type": "Sequence",
        "children": [
          {"type": "Condition", "value": "is_grounded == 0"},
          {"type": "Condition", "value": "left_retracted == 1"},
          {"type": "Action", "value": "launch_left_at_opponent"}
        ]
      },
      {
        "type": "Sequence",
        "children": [
          {"type": "Condition", "value": "is_grounded == 0"},
          {"type": "Condition", "value": "right_retracted == 1"},
          {"type": "Action", "value": "launch_right_at_opponent"}
        ]
      },
      {
        "type": "Sequence",
        "children": [
          {"type": "Condition", "value": "is_grounded == 1"},
          {"type": "Action", "value": "jump"}
        ]
      },
      {"type": "Action", "value": "move_toward_opponent"}
    ]
  }
]
</tree>

Battle log summaries (7 representative matches):
<logs>
{"match_id":"gen_000_match_004_g1","opponent":"Red_CounterStriker","result":"loss","duration_ticks":447,"final_state":{"fighter_hp":0,"opponent_hp":40},"damage_summary":{"dealt":60,"received":108,"hits_landed":5,"hits_taken":9,"launches_at_opponent":23,"hit_accuracy":0.2174},"action_frequency":{"launch_left_at_opponent":14,"launch_right_at_opponent":9,"move_toward_opponent":393,"jump":10,"lock_left":3,"retract_left":3,"lock_right":3,"retract_right":3,"launch_right_down":6,"launch_left_down":3},"positional_summary":{"time_grounded_pct":0.0224,"time_airborne_pct":0.9776,"time_near_opponent_pct":0.6935,"avg_distance_to_opponent":170.00},"grapple_stats":{"attach_count":6,"avg_attached_duration_ticks":8.67,"ceiling_attaches":0,"wall_attaches":6},"phase_breakdown":[{"phase":"early","damage_dealt":24,"damage_received":12,"hp_at_end":[88,76]},{"phase":"mid","damage_dealt":24,"damage_received":24,"hp_at_end":[64,52]},{"phase":"late","damage_dealt":12,"damage_received":72,"hp_at_end":[0,40]}]}
---
{"match_id":"gen_000_match_011_g1","opponent":"Blue_SwingShotgun","result":"loss","duration_ticks":272,"final_state":{"fighter_hp":0,"opponent_hp":16},"damage_summary":{"dealt":84,"received":108,"hits_landed":7,"hits_taken":9,"launches_at_opponent":20,"hit_accuracy":0.35},"action_frequency":{"launch_left_at_opponent":10,"launch_right_at_opponent":10,"move_toward_opponent":228,"jump":7,"lock_left":2,"retract_left":2,"lock_right":2,"retract_right":2,"launch_left_down":4,"launch_right_down":5},"positional_summary":{"time_grounded_pct":0.0257,"time_airborne_pct":0.9743,"time_near_opponent_pct":0.8199,"avg_distance_to_opponent":142.89},"grapple_stats":{"attach_count":4,"avg_attached_duration_ticks":8.25,"ceiling_attaches":0,"wall_attaches":4},"phase_breakdown":[{"phase":"early","damage_dealt":12,"damage_received":36,"hp_at_end":[64,88]},{"phase":"mid","damage_dealt":48,"damage_received":60,"hp_at_end":[4,40]},{"phase":"late","damage_dealt":24,"damage_received":12,"hp_at_end":[0,16]}]}
---
{"match_id":"gen_000_match_013_g1","opponent":"Cyan_ZoneController","result":"win","duration_ticks":347,"final_state":{"fighter_hp":52,"opponent_hp":0},"damage_summary":{"dealt":108,"received":48,"hits_landed":9,"hits_taken":4,"launches_at_opponent":26,"hit_accuracy":0.3462},"action_frequency":{"launch_left_at_opponent":14,"launch_right_at_opponent":12,"move_toward_opponent":290,"jump":8,"lock_left":5,"retract_left":5,"lock_right":4,"retract_right":4,"launch_left_down":3,"launch_right_down":2},"positional_summary":{"time_grounded_pct":0.0231,"time_airborne_pct":0.9769,"time_near_opponent_pct":0.5793,"avg_distance_to_opponent":209.60},"grapple_stats":{"attach_count":9,"avg_attached_duration_ticks":8.78,"ceiling_attaches":0,"wall_attaches":9},"phase_breakdown":[{"phase":"early","damage_dealt":60,"damage_received":0,"hp_at_end":[100,40]},{"phase":"mid","damage_dealt":0,"damage_received":48,"hp_at_end":[52,40]},{"phase":"late","damage_dealt":48,"damage_received":0,"hp_at_end":[52,0]}]}
---
{"match_id":"gen_000_match_015_g1","opponent":"Magenta_SwingSniper","result":"loss","duration_ticks":328,"final_state":{"fighter_hp":0,"opponent_hp":76},"damage_summary":{"dealt":24,"received":108,"hits_landed":2,"hits_taken":9,"launches_at_opponent":16,"hit_accuracy":0.125},"action_frequency":{"launch_left_at_opponent":7,"launch_right_at_opponent":9,"move_toward_opponent":288,"jump":8,"lock_left":2,"retract_left":2,"lock_right":2,"retract_right":2,"launch_left_down":5,"launch_right_down":3},"positional_summary":{"time_grounded_pct":0.0244,"time_airborne_pct":0.9756,"time_near_opponent_pct":0.6738,"avg_distance_to_opponent":162.88},"grapple_stats":{"attach_count":4,"avg_attached_duration_ticks":8.5,"ceiling_attaches":0,"wall_attaches":4},"phase_breakdown":[{"phase":"early","damage_dealt":12,"damage_received":60,"hp_at_end":[40,88]},{"phase":"mid","damage_dealt":0,"damage_received":24,"hp_at_end":[16,88]},{"phase":"late","damage_dealt":12,"damage_received":24,"hp_at_end":[0,76]}]}
---
{"match_id":"gen_000_match_008_g3","opponent":"Green_GrappleAssassin","result":"win","duration_ticks":384,"final_state":{"fighter_hp":40,"opponent_hp":0},"damage_summary":{"dealt":108,"received":60,"hits_landed":9,"hits_taken":5,"launches_at_opponent":24,"hit_accuracy":0.375},"action_frequency":{"launch_left_at_opponent":12,"launch_right_at_opponent":12,"move_toward_opponent":334,"jump":7,"lock_left":2,"retract_left":2,"lock_right":2,"retract_right":2,"launch_left_down":6,"launch_right_down":5},"positional_summary":{"time_grounded_pct":0.0182,"time_airborne_pct":0.9818,"time_near_opponent_pct":0.8776,"avg_distance_to_opponent":170.24},"grapple_stats":{"attach_count":4,"avg_attached_duration_ticks":8.5,"ceiling_attaches":0,"wall_attaches":4},"phase_breakdown":[{"phase":"early","damage_dealt":12,"damage_received":12,"hp_at_end":[88,88]},{"phase":"mid","damage_dealt":48,"damage_received":12,"hp_at_end":[76,40]},{"phase":"late","damage_dealt":48,"damage_received":36,"hp_at_end":[40,0]}]}
---
{"match_id":"gen_000_match_013_g6","opponent":"Cyan_ZoneController","result":"win","duration_ticks":584,"final_state":{"fighter_hp":4,"opponent_hp":0},"damage_summary":{"dealt":108,"received":96,"hits_landed":9,"hits_taken":8,"launches_at_opponent":39,"hit_accuracy":0.2308},"action_frequency":{"launch_left_at_opponent":19,"launch_right_at_opponent":20,"move_toward_opponent":493,"jump":12,"lock_left":7,"retract_left":7,"lock_right":6,"retract_right":6,"launch_left_down":7,"launch_right_down":7},"positional_summary":{"time_grounded_pct":0.0223,"time_airborne_pct":0.9777,"time_near_opponent_pct":0.6866,"avg_distance_to_opponent":180.02},"grapple_stats":{"attach_count":13,"avg_attached_duration_ticks":10.0,"ceiling_attaches":0,"wall_attaches":13},"phase_breakdown":[{"phase":"early","damage_dealt":36,"damage_received":36,"hp_at_end":[64,64]},{"phase":"mid","damage_dealt":48,"damage_received":36,"hp_at_end":[28,16]},{"phase":"late","damage_dealt":24,"damage_received":24,"hp_at_end":[4,0]}]}
---
{"match_id":"gen_000_match_004_g2","opponent":"Red_CounterStriker","result":"loss","duration_ticks":212,"final_state":{"fighter_hp":0,"opponent_hp":88},"damage_summary":{"dealt":12,"received":108,"hits_landed":1,"hits_taken":9,"launches_at_opponent":11,"hit_accuracy":0.0909},"action_frequency":{"launch_left_at_opponent":6,"launch_right_at_opponent":5,"move_toward_opponent":176,"jump":6,"lock_left":3,"retract_left":3,"lock_right":3,"retract_right":3,"launch_left_down":4,"launch_right_down":3},"positional_summary":{"time_grounded_pct":0.0377,"time_airborne_pct":0.9623,"time_near_opponent_pct":0.5566,"avg_distance_to_opponent":191.49},"grapple_stats":{"attach_count":6,"avg_attached_duration_ticks":8.67,"ceiling_attaches":0,"wall_attaches":6},"phase_breakdown":[{"phase":"early","damage_dealt":12,"damage_received":0,"hp_at_end":[100,88]},{"phase":"mid","damage_dealt":0,"damage_received":96,"hp_at_end":[4,88]},{"phase":"late","damage_dealt":0,"damage_received":12,"hp_at_end":[0,88]}]}
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

Keep the report under 300 words.
