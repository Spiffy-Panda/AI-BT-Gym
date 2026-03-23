# Behavior Tree Evolution Gym — LLM-Mediated Plan

## Overview

A population of **5–10 behavior trees** (BTs) compete in rounds of simulated fights. An LLM evaluates replays, assigns Elo ratings, and drives three evolutionary operations — **Improve**, **Mutate**, and **Crossbreed** — to produce new generations. The system runs in repeating **seasons**, each containing multiple phases.

---

## 1  Population & Representation

Each individual in the pool is stored as a JSON behavior tree plus metadata:

```json
{
  "id": "bt_07",
  "elo": 1200,
  "generation": 3,
  "lineage": ["bt_02", "bt_05"],
  "tree": { "...BT JSON..." },
  "notes": "Aggressive opener, weak recovery after failed combo."
}
```

**Pool bounds:** minimum 5, maximum 10. The system targets **8** as the steady-state size and trims or fills to stay in range after each generation cycle.

---

## 2  Season Structure

A **season** is one full loop of play → evaluate → evolve. Repeat indefinitely.

| Phase | Name | What happens | LLM involved? |
|-------|------|-------------|----------------|
| A | **Round-Robin Play** | Every BT fights every other BT (or a sampled subset). Fights produce structured replay logs. | No |
| B | **Replay Analysis** | LLM reads each replay, identifies strengths/weaknesses per BT, writes a short scouting report. | **Yes** |
| C | **Elo Update** | Win/loss/draw outcomes update Elo ratings using standard Elo math (K=32). | No (deterministic) |
| D | **Cull** | Remove the weakest individuals to make room. | Partially (LLM confirms or overrides) |
| E | **Evolve** | Produce new BTs via Improve, Mutate, and Crossbreed to refill the pool. | **Yes** |
| F | **Intake Validation** | Sanity-check new BTs (parse, dry-run a short fight). Discard malformed trees and re-roll. | No |

### Phase Timing

- **Rounds per season:** Each BT plays **3–5 fights** minimum before any culling happens. With a pool of 8 that means a full round-robin (28 matches) or a sampled schedule of ~20 matches.
- **Seasons are not time-boxed.** A season ends when all six phases complete.

---

## 3  Phase A — Round-Robin Play

Run the game engine headless. For each match, emit a **replay log** — a structured list of timestamped events:

```
[0.0s] bt_03 starts at position (0,0); bt_07 starts at (10,0)
[0.3s] bt_03 executes: Approach(target=bt_07)
[0.8s] bt_07 executes: Guard(stance=high)
[1.1s] bt_03 executes: Attack(type=sweep, damage=12)
[1.1s] bt_07: Guard FAILED (sweep vs high guard)
[1.1s] bt_07 takes 12 damage (HP: 88/100)
...
[14.2s] RESULT: bt_03 wins (bt_07 HP=0)
```

If the pool has N individuals, the full round-robin is N×(N−1)/2 matches. For pools >8, sample ~30 matches using a balanced schedule so every BT plays at least 4 times.

---

## 4  Phase B — Replay Analysis (LLM)

Feed each BT's collected replays to the LLM and ask for a **scouting report**.

### Prompt: Scouting Report

```
You are an analyst for a behavior-tree fighting AI tournament.

Below are the replay logs for contestant {{ bt.id }} (Elo {{ bt.elo }}).
Its behavior tree structure is:
<tree>
{{ bt.tree | json }}
</tree>

Replays:
<replays>
{{ bt.replays | join("\n---\n") }}
</replays>

Write a scouting report covering:
1. **Strengths** — tactics or sequences that consistently work.
2. **Weaknesses** — situations where the BT fails, freezes, or makes bad trades.
3. **Matchup notes** — which opponents give it trouble and why.
4. **Suggested fix** — one concrete, targeted change to the tree that would
   address the single biggest weakness. Describe it in plain English, not code.

Keep the report under 300 words.
```

The scouting report is saved in the BT's `notes` field and fed forward into Phase E.

---

## 5  Phase C — Elo Update

Standard Elo with **K = 32**:

```
expected_a = 1 / (1 + 10^((elo_b - elo_a) / 400))
new_elo_a  = elo_a + K * (outcome_a - expected_a)
```

- Win = 1.0, Loss = 0.0, Draw = 0.5
- New BTs enter at **Elo 1200** (the pool median is anchored near 1200).

No LLM involvement — this is pure math.

---

## 6  Phase D — Cull

**Goal:** Reduce pool to **target − slots_to_fill**. Typically remove 2–3 individuals per season.

### Selection for removal

1. **Hard cut:** Any BT with Elo in the bottom 20% of the pool AND that has existed for ≥2 seasons is eligible.
2. **Staleness cut:** Any BT unchanged for ≥4 seasons (never selected for improvement) is eligible regardless of Elo.
3. **Diversity protection:** Never remove the *last* representative of a distinct lineage branch. (Track via `lineage` field — if removing a BT would leave zero BTs sharing any ancestor with it, skip it.)

If more candidates than needed, remove lowest-Elo first.

### Prompt: Cull Override Check

Before removing, optionally ask the LLM to sanity-check:

```
The following BTs are scheduled for removal from the pool due to low
performance:

{{ cull_candidates | map("id + ' Elo:' + elo + ' — ' + notes[:80]") | join("\n") }}

Current pool diversity snapshot:
- Aggressive openers: {{ count_aggressive }}
- Defensive/reactive: {{ count_defensive }}
- Mixed/adaptive: {{ count_mixed }}

Should any of these be kept to preserve strategic diversity?
Reply with a JSON list of IDs to KEEP, or an empty list to proceed.
```

This is a soft gate. The system respects the LLM's keep-list but never lets the pool exceed 10.

---

## 7  Phase E — Evolve (LLM-Heavy)

After culling, calculate how many **slots** are open (target 8 minus current count). Fill them using a weighted mix of three operations:

| Operation | Weight | When to prefer |
|-----------|--------|----------------|
| **Improve** | 40% | The source BT has a clear weakness identified in scouting |
| **Mutate** | 30% | Inject novelty; source is decent but the pool is stagnating |
| **Crossbreed** | 30% | Two parents have complementary strengths |

Round fractional slots toward whichever operation hasn't run recently. Minimum one of each per every 3 seasons.

---

### 7a  Improve

Pick a mid-tier BT (Elo rank 3–6) with a clear weakness. Ask the LLM to fix the identified problem.

#### Prompt: Improve

```
You are a behavior tree engineer. Your job is to fix a specific weakness in a
fighting AI's behavior tree.

Here is the current tree:
<tree>
{{ bt.tree | json }}
</tree>

Scouting report:
<report>
{{ bt.notes }}
</report>

The single most important problem to fix:
"{{ bt.notes.suggested_fix }}"

Return ONLY the complete, corrected behavior tree as valid JSON.
Preserve everything that already works — change as little as possible to
address the stated problem. Do not add unrelated behaviors.
```

The resulting BT inherits the parent's `lineage` plus its own new ID. Starting Elo is the **parent's Elo minus 50** (slight penalty so it must prove itself).

---

### 7b  Mutate

Pick any BT in the top 60% by Elo. Ask the LLM to add or remove a novel sub-behavior.

#### Prompt: Mutate (Add)

```
You are designing a novel tactic for a behavior tree fighting AI.

Here is the current tree:
<tree>
{{ bt.tree | json }}
</tree>

This fighter is solid but predictable. Invent ONE new behavior or sub-tree to
add that would surprise opponents. It should:
- Be a coherent, self-contained sub-tree (1–4 nodes).
- Fit naturally into the existing tree structure.
- Not duplicate anything already present.

Describe the new sub-tree in plain English first (2–3 sentences), then return
the full updated tree as valid JSON.
```

#### Prompt: Mutate (Subtract)

```
You are simplifying a behavior tree fighting AI that has grown too complex.

Here is the current tree ({{ bt.tree | node_count }} nodes):
<tree>
{{ bt.tree | json }}
</tree>

Scouting report:
<report>
{{ bt.notes }}
</report>

Identify the sub-tree that contributes the LEAST to winning and remove it.
Explain in 1–2 sentences why you chose it, then return the pruned tree as
valid JSON.
```

Mutants start at **Elo 1200** (pool median) since they're novel enough to need fresh evaluation.

---

### 7c  Crossbreed

Pick two parents: one from the **top 3** by Elo, one from the **top 6** (can overlap). Ask the LLM to combine high-level strategy from one with low-level execution from the other.

#### Prompt: Crossbreed

```
You are breeding a new behavior tree fighting AI from two parents.

Parent A ({{ parent_a.id }}, Elo {{ parent_a.elo }}):
<tree_a>
{{ parent_a.tree | json }}
</tree_a>
Scouting report A: {{ parent_a.notes }}

Parent B ({{ parent_b.id }}, Elo {{ parent_b.elo }}):
<tree_b>
{{ parent_b.tree | json }}
</tree_b>
Scouting report B: {{ parent_b.notes }}

Create a NEW behavior tree that:
1. Takes the HIGH-LEVEL STRATEGY (root-level priorities, phase transitions,
   win-condition approach) from Parent {{ strategy_donor }}.
2. Takes LOW-LEVEL IMPLEMENTATIONS (specific attack combos, guard timings,
   movement patterns) from Parent {{ tactics_donor }}.
3. Resolves any conflicts sensibly — explain your reasoning in 2–3 sentences
   before the JSON.

Return the child tree as valid JSON after your explanation.
```

`strategy_donor` and `tactics_donor` are assigned semi-randomly: 60% chance the higher-Elo parent donates strategy, 40% reversed (to avoid convergence).

Child starts at the **average of the parents' Elos minus 25**.

---

## 8  Phase F — Intake Validation

For every new BT produced in Phase E:

1. **Parse check** — Is the JSON a valid behavior tree? Does every node reference valid actions?
2. **Dry-run** — Run one short fight against a random pool member. Does the BT execute without crashing? Does it take at least one action per second?
3. **Degenerate check** — Does the BT do the exact same thing every fight? (Run 3 micro-fights; if the action log is identical across all 3, reject as degenerate.)

If a BT fails validation, re-request from the LLM **once** with the error message appended:

```
Your previous output failed validation:
{{ error_message }}

Please fix the issue and return a corrected tree as valid JSON.
```

If it fails a second time, discard it and the slot stays open until next season.

---

## 9  Elo Seeding & New Entrant Rules

| Origin | Starting Elo |
|--------|-------------|
| Improve | Parent Elo − 50 |
| Mutate | 1200 (pool median) |
| Crossbreed | avg(Parent A, Parent B) − 25 |
| Manual injection | 1200 |

New entrants get a **K-factor boost** of K=48 for their first 10 matches (instead of 32) so their rating stabilizes faster.

---

## 10  Stagnation Detection & Escape

After every 3 seasons, check for stagnation:

- **Elo compression:** If the spread (max Elo − min Elo) is < 80, the pool is converging.
- **Archetype collapse:** If >70% of BTs share >60% of their sub-trees (measured by tree edit distance), diversity is too low.

### Prompt: Wildcard Injection

When stagnation is detected, skip normal evolution and ask for a completely novel BT:

```
The current behavior tree fighting AI pool has become stagnant — most
fighters use similar strategies. Here is a summary of the dominant style:

{{ dominant_style_summary }}

Design a behavior tree from scratch that uses a FUNDAMENTALLY DIFFERENT
strategy. Think about unexplored approaches: extreme aggression, extreme
evasion, bait-and-punish, resource denial, tempo manipulation, etc.

Do NOT use any of these sub-trees that are already overrepresented:
{{ overrepresented_subtrees | join(", ") }}

Return the tree as valid JSON.
```

Wildcards enter at Elo 1200 and get the K=48 new-entrant boost.

---

## 11  Full Season Flow (Summary)

```
SEASON N
│
├─ A. Round-Robin Play ────────────────────── [no LLM]
│     every BT fights 4+ matches
│
├─ B. Replay Analysis ─────────────────────── [LLM: scouting reports]
│     1 prompt per BT
│
├─ C. Elo Update ──────────────────────────── [no LLM]
│     standard Elo math
│
├─ D. Cull ─────────────────────────────────── [LLM: optional override]
│     remove 2–3 weakest / stalest
│     pool drops to 5–6
│
├─ E. Evolve ───────────────────────────────── [LLM: 2–3 prompts]
│     fill back to 8 via Improve / Mutate / Crossbreed
│
├─ F. Intake Validation ───────────────────── [no LLM, or 1 retry prompt]
│     parse, dry-run, degenerate check
│
└─ → SEASON N+1
```

**LLM calls per season (pool of 8):**
- Scouting reports: 8
- Cull check: 1
- Evolution operations: 2–3
- Validation retries: 0–2
- **Total: ~12–14 LLM calls per season**

---

## 12  Tuning Knobs

| Parameter | Default | What it controls |
|-----------|---------|-----------------|
| `pool_target` | 8 | Steady-state population size |
| `pool_min` / `pool_max` | 5 / 10 | Hard bounds |
| `matches_per_season` | round-robin | How many fights before evolving |
| `K_factor` | 32 | Elo volatility |
| `K_factor_newbie` | 48 | Elo volatility for first 10 matches |
| `cull_count` | 2–3 | How many to remove per season |
| `improve_weight` | 0.4 | Fraction of slots filled by Improve |
| `mutate_weight` | 0.3 | Fraction of slots filled by Mutate |
| `crossbreed_weight` | 0.3 | Fraction of slots filled by Crossbreed |
| `stagnation_check_interval` | 3 seasons | How often to check for convergence |
| `elo_spread_threshold` | 80 | Below this, pool is stagnant |
| `min_seasons_before_cull` | 2 | Grace period for new BTs |
