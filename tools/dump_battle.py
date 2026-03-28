import sys, json

d = json.load(sys.stdin)
af = d.get('action_frequency', {})
gs = d.get('grapple_stats', {})
ps = d.get('positional_summary', {})
ds = d.get('damage_summary', {})

print('Actions:')
for k, v in sorted(af.items(), key=lambda x: -x[1]):
    print(f'  {k}: {v}')

attach = gs.get('attach_count', 0)
ceiling = gs.get('ceiling_attaches', 0)
wall = gs.get('wall_attaches', 0)
print(f'Grapple: attaches={attach}, ceiling={ceiling}, wall={wall}')

grounded = ps.get('time_grounded_pct', 0) * 100
dist = ps.get('avg_distance_to_opponent', 0)
print(f'Grounded: {grounded:.1f}%, Avg distance: {dist:.0f}')

dealt = ds.get('dealt', 0)
received = ds.get('received', 0)
acc = ds.get('hit_accuracy', 0) * 100
print(f'Damage: dealt={dealt}, received={received}, accuracy={acc:.1f}%')
