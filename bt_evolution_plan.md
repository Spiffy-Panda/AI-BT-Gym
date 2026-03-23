# Behavior Tree Evolution Gym — Chain-Fist Fighter Plan

## Overview

A population of **5–10 behavior trees** (BTs) compete in rounds of simulated 1v1 chain-fist fights. An LLM evaluates battle logs, assigns Elo ratings, and drives three evolutionary operations — **Improve**, **Mutate**, and **Crossbreed** — to produce new generations. The system runs in repeating **seasons**, each containing multiple phases.

---

## 1  Game Mechanics Reference

### Arena
- **5000 × 400 px** playfield with 10px walls
- Fighters spawn at 25% from each edge, on ground level
- Boundary: position clamped, velocity zeroed on contact

### Fighter State
| Property | Details |
|----------|---------|
| **HP** | 100, reduced by fist hits (12 per hit), KO at 0 |
| **Position** | Vector2 (X, Y) within arena bounds |
| **Velocity** | Vector2, clamped to ±350 px/s horizontal |
| **IsGrounded** | True when on ground surface |
| **BodyRadius** | 18 px (collision sphere) |
| **Fists** | Two independent chain-fists (Left, Right) |

### Chain-Fist System

Each fighter has **two independent fists** on extendable chains. Each fist is a 4-state machine:

```
Retracted ──[launch]──▶ Extending ──[lock]──▶ Locked ──[retract]──▶ Retracting ──▶ Retracted
                              │                                          │
                              └── auto-retracts at max chain length ─────┘
```

| State | Behavior |
|-------|----------|
| **Retracted** | Fist at body, chain length = 0, ready to launch |
| **Extending** | Fist flies outward at 900 px/s, max range 280 px |
| **Locked** | Fist anchored mid-air; body swings as pendulum |
| **Retracting** | Chain pulls back at 700 px/s; if anchored, body pulled toward anchor at 1800 force |

**Key mechanic:** Fists anchor mid-air (no surface required). A locked fist creates a pendulum pivot — the fighter swings from it under gravity. Retracting an anchored fist pulls the fighter toward the anchor point (grapple).

### Physics
| Constant | Value |
|----------|-------|
| Gravity | 980 px/s² |
| Tick rate | 60 FPS (1/60s fixed step) |
| Ground move force | 400 |
| Air move force | 200 |
| Jump impulse | -420 (upward) |
| Ground friction | 0.85× per frame |
| Air friction | 0.995× per frame |
| Swing damping | 0.995× per frame |
| Fist damage | 12 HP |
| Knockback | 250 magnitude |

### Collision
- **Fist-vs-Fist:** Both forced to retract (cancel each other)
- **Fist-vs-Body:** 12 damage + knockback; fist forced to retract

### Match Rules
- **Duration:** 3600 ticks (60 seconds)
- **Win by KO:** Opponent HP ≤ 0
- **Win by timeout:** Higher remaining HP wins; equal HP = draw

---

## 2  Available BT Actions

### Fist Launching
| Action | Direction |
|--------|-----------|
| `launch_left_at_opponent` / `launch_right_at_opponent` | Calculated aim |
| `launch_left_up` / `launch_right_up` | Straight up (0, -1) |
| `launch_left_upleft` / `launch_right_upright` | Diagonal up |
| `launch_left_down` / `launch_right_down` | Straight down (0, 1) |

### Fist State Control
| Action | Effect |
|--------|--------|
| `lock_left` / `lock_right` | Lock extending chain → create anchor |
| `retract_left` / `retract_right` | Begin retracting (grapple-pull if anchored) |
| `detach_left` / `detach_right` | Release world attachment |

### Movement
| Action | Effect |
|--------|--------|
| `move_left` / `move_right` | Horizontal force |
| `move_toward_opponent` / `move_away_from_opponent` | Dynamic direction |
| `jump` | Vertical impulse (grounded only) |

All actions return Success/Failure based on state preconditions (e.g. `launch_left_at_opponent` fails if left fist is not retracted).

---

## 3  Available BT Conditions

### Fighter State
`health`, `opponent_health` (0–100), `pos_x`, `pos_y`, `vel_x`, `vel_y`, `opponent_pos_x`, `opponent_pos_y`, `is_grounded` (0/1), `distance_to_opponent`

### Fist State (0=Retracted, 1=Extending, 2=Locked, 3=Retracting)
`left_state`, `right_state`, `left_attached`, `right_attached` (0/1), `left_retracted`, `right_retracted` (0/1), `left_chain`, `right_chain` (current length)

### Opponent Fist State
`opp_left_state`, `opp_right_state`

### Direction
`opponent_dir_x`, `opponent_dir_y` (±1)

### Condition Syntax
- Comparisons: `<`, `<=`, `>`, `>=`, `==`, `!=`
- Math: `+`, `-`, `*`, `/` (left-to-right)
- Literals: `always`, `never`, `true`, `false`
- Example: `distance_to_opponent < 200`, `health + 10 > opponent_health * 0.5`

### Pre-Built Condition Shorthands (Spec.cs)
`Grounded`, `Airborne`, `Ascending`, `Descending`, `InRange(x)`, `OutOfRange(x)`, `InMeleeRange`, `InAttackRange`, `InPokeRange`, `LeftReady`, `RightReady`, `LeftExtending`, `LeftLocked`, `LeftRetracting`, `LeftAnchored`, `RightAnchored`, `LeftChainOver(len)`, `RightChainOver(len)`

---

## 4  BT Node Types

| Category | Nodes |
|----------|-------|
| **Composites** | Sequence (all succeed), Selector (first success), Parallel (policy-based) |
| **Decorators** | Inverter (flip), Repeater (loop N), Cooldown (block N ticks), ConditionGate (guard) |
| **Leaves** | Condition (evaluate expression), Action (execute, return Success/Failure) |

**Execution model:** Stateless per tick — re-evaluates from root every frame. Only mutable state is cooldown expiry tracking. No "Running" status.

---

## 5  Known Archetypes (Seed Fighters)

These six seed AIs define the initial strategic landscape. Evolution should discover variants, counters, and hybrids.

| Name | Strategy | Key Mechanics |
|------|----------|---------------|
| **CounterStriker** | Mid-range reactive; dodges and punishes whiffs | Waits for opponent extension, then strikes |
| **GrappleAssassin** | Ceiling grapple → swing → dive strike | One fist anchors high, other fist attacks during dive |
| **SwingShotgun** | Sequential mid-air anchors; fires while swinging | Alternating fist usage for continuous pressure |
| **ZoneController** | Staggered fist pressure; optimal spacing | Never stops attacking; denies approach |
| **DiveKicker** | Jump-heavy; brief locks for hang time; downward strikes | Aerial dominance through repeated dives |
| **SwingSniper** | Pendulum specialist; precise arc-timed strikes | Swing momentum for hit-and-run |

---

## 6  Population & Representation

Each individual in the pool is stored as a JSON behavior tree plus metadata:

```json
{
  "id": "bt_07",
  "elo": 1200,
  "generation": 3,
  "lineage": ["bt_02", "bt_05"],
  "tree": { "...BT JSON..." },
  "notes": "Strong ceiling grapple game, but collapses when both fists are retracting simultaneously."
}
```

**Pool bounds:** minimum 5, maximum 10. Target **8** steady-state. Trim or fill after each generation cycle.

---

## 7  Season Structure

A **season** is one full loop of play → evaluate → evolve. Repeat indefinitely.

| Phase | Name | What happens | LLM? |
|-------|------|-------------|-------|
| A | **Round-Robin Play** | Every BT fights every other BT. Matches produce battle logs with per-tick state, hit events, grapple stats, and phase breakdowns. | No |
| B | **Battle Log Analysis** | LLM reads each BT's battle logs, identifies chain-fist usage patterns, grapple efficiency, and tactical weaknesses. Writes scouting report. | **Yes** |
| C | **Elo Update** | Win/loss/draw outcomes update Elo (K=32). | No |
| D | **Cull** | Remove weakest individuals to make room. | Partially |
| E | **Evolve** | Produce new BTs via Improve, Mutate, Crossbreed. | **Yes** |
| F | **Intake Validation** | Parse-check, dry-run fight, degenerate check on new BTs. | No |

---

## 8  Phase A — Round-Robin Play

Run headless via:
```bash
"/c/Program Files/godot/godot_console.exe" --headless --scene res://scenes/tournament.tscn
```

For each match, the MatchRecorder emits a structured battle log containing:

- **Per-tick state:** positions, velocities, HP, grounded status (checkpointed every 10 ticks)
- **Hit events:** tick, attacker, hand (left/right), damage dealt, positions at impact
- **Action frequency:** count of each action executed per fighter
- **Grapple stats:** attach count, average duration, ceiling vs wall classification
- **Phase breakdown:** early/mid/late damage splits and dominant actions per phase
- **Key moments:** first blood tick, health crossover ticks, KO tick

Full round-robin for N=8 is 28 matches. For pools >8, sample ~30 balanced matches (every BT plays ≥4).

### Example Battle Log Excerpt
```
[tick 0] Red spawns at (1250, 380); Blue spawns at (3750, 380)
[tick 18] Red: launch_right_at_opponent → Extending toward Blue
[tick 25] Blue: launch_left_up → Extending upward
[tick 31] Blue: lock_left → Locked (anchor at 3720, 50), attached to world
[tick 32] Blue: swing begins, pendulum from ceiling anchor
[tick 38] Red: right_fist extending, chain=252/280
[tick 40] Red: right_fist auto-retract (max chain)
[tick 45] Blue: launch_right_at_opponent → Extending toward Red during downswing
[tick 48] HIT: Blue right_fist → Red body, 12 damage (Red HP: 88)
[tick 48] Blue: right_fist forced retract; Red knockback applied
[tick 55] Blue: retract_left → Retracting (grapple pull toward ceiling anchor)
...
[tick 2400] RESULT: Blue wins by KO (Red HP: 0, Blue HP: 64)
```

---

## 9  Phase B — Battle Log Analysis (LLM)

### Prompt: Scouting Report

```
You are analyzing a chain-fist fighting AI in a 2D arena tournament.

Fighters control two independent chain-fists that can be launched, locked mid-air
as pendulum anchors, and retracted to grapple-pull the body. Fist hits deal 12 damage.
Key tactics: ceiling grappling, pendulum swings, dive attacks, zoning with staggered
fists, and punishing overextended chains.

Below are the battle logs for contestant {{ bt.id }} (Elo {{ bt.elo }}).
Its behavior tree structure is:
<tree>
{{ bt.tree | json }}
</tree>

Battle log summaries:
<logs>
{{ bt.battle_logs | join("\n---\n") }}
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
```

---

## 10  Phase C — Elo Update

Standard Elo with **K = 32**:
```
expected_a = 1 / (1 + 10^((elo_b - elo_a) / 400))
new_elo_a  = elo_a + K * (outcome_a - expected_a)
```
- Win = 1.0, Loss = 0.0, Draw = 0.5
- New BTs enter at Elo 1200.

---

## 11  Phase D — Cull

**Goal:** Reduce pool to target − slots_to_fill. Typically remove 2–3 per season.

### Selection for removal
1. **Hard cut:** Elo in bottom 20% AND existed ≥2 seasons.
2. **Staleness cut:** Unchanged for ≥4 seasons, regardless of Elo.
3. **Diversity protection:** Never remove the last representative of a distinct lineage branch.

If more candidates than needed, remove lowest-Elo first.

### Prompt: Cull Override Check

```
The following BTs are scheduled for removal from the chain-fist fighting pool:

{{ cull_candidates | format }}

Current pool archetype diversity:
- Grapplers (ceiling/swing-heavy): {{ count_grapplers }}
- Ground fighters (movement/spacing): {{ count_ground }}
- Divers (aerial strike-heavy): {{ count_divers }}
- Zoners (fist-pressure/spacing): {{ count_zoners }}
- Hybrid/adaptive: {{ count_hybrid }}

Should any be kept to preserve strategic diversity?
Reply with a JSON list of IDs to KEEP, or empty list to proceed.
```

---

## 12  Phase E — Evolve (LLM-Heavy)

Fill open slots (target 8 minus current) using weighted operations:

| Operation | Weight | When to prefer |
|-----------|--------|----------------|
| **Improve** | 40% | Clear weakness identified in scouting |
| **Mutate** | 30% | Pool stagnating; inject novelty |
| **Crossbreed** | 30% | Two parents have complementary strengths |

Minimum one of each per every 3 seasons.

---

### 12a  Improve

Pick a mid-tier BT (Elo rank 3–6) with a clear weakness.

#### Prompt: Improve

```
You are a behavior tree engineer for a chain-fist fighting AI.

Fighters have two independent chain-fists. Each can be launched, locked mid-air
as a pendulum anchor, or retracted to grapple-pull the body. Fist-body hits
deal 12 damage with knockback.

Available actions:
- Launch: launch_{left|right}_{at_opponent|up|upleft|upright|down}
- Lock: lock_{left|right} (creates mid-air anchor)
- Retract: retract_{left|right} (grapple-pull if anchored)
- Detach: detach_{left|right} (release anchor without pull)
- Move: move_{left|right|toward_opponent|away_from_opponent}, jump

Available conditions:
- distance_to_opponent, health, opponent_health, is_grounded
- left_state/right_state (0=Retracted,1=Extending,2=Locked,3=Retracting)
- left_attached/right_attached, left_retracted/right_retracted
- left_chain/right_chain (current length), pos_x, pos_y, vel_x, vel_y

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
Preserve everything that already works — change as little as possible.
```

Starting Elo: parent's Elo − 50.

---

### 12b  Mutate

Pick any BT in the top 60% by Elo.

#### Prompt: Mutate (Add)

```
You are designing a novel tactic for a chain-fist fighting AI.

Here is the current tree:
<tree>
{{ bt.tree | json }}
</tree>

This fighter is solid but predictable. Invent ONE new chain-fist behavior or
sub-tree that would surprise opponents. Consider unexplored possibilities:

- Fist-cancel feints (launch and immediately retract to bait reactions)
- Double-anchor ceiling crawl (alternate fists along ceiling)
- Pendulum momentum attacks (time strikes at peak swing velocity)
- Ground-to-air transition combos (jump → lock → swing → strike)
- Defensive anchor placement (lock fist behind to escape backward)
- Fist-vs-fist denial (intercept opponent's extending fist)

The new sub-tree should be 1–4 nodes, self-contained, and fit naturally
into the existing tree.

Describe the new tactic in 2–3 sentences, then return the full updated tree
as valid JSON.
```

#### Prompt: Mutate (Subtract)

```
You are simplifying a chain-fist fighting AI that has grown too complex.

Here is the current tree ({{ bt.tree | node_count }} nodes):
<tree>
{{ bt.tree | json }}
</tree>

Scouting report:
<report>
{{ bt.notes }}
</report>

Identify the sub-tree that contributes LEAST to winning. Consider:
- Branches that rarely activate (conditions too specific)
- Redundant fist-management logic
- Movement patterns that conflict with grapple behavior

Explain in 1–2 sentences why you chose it, then return the pruned tree as
valid JSON.
```

Starting Elo: 1200 (pool median).

---

### 12c  Crossbreed

Pick two parents: one from top 3 by Elo, one from top 6.

#### Prompt: Crossbreed

```
You are breeding a new chain-fist fighting AI from two parents.

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
1. Takes the HIGH-LEVEL STRATEGY (when to be grounded vs aerial, when to
   grapple vs zone, engagement distance preferences) from Parent {{ strategy_donor }}.
2. Takes LOW-LEVEL CHAIN-FIST TACTICS (specific launch sequences, anchor
   placement patterns, swing-attack timing, retract combos) from
   Parent {{ tactics_donor }}.
3. Resolves conflicts sensibly — e.g. if one parent is a ground fighter and
   the other a ceiling grappler, explain how the child blends these.

Return the child tree as valid JSON after your 2–3 sentence explanation.
```

Starting Elo: avg(Parent A, Parent B) − 25.
Strategy donor: 60% higher-Elo parent, 40% reversed.

---

## 13  Phase F — Intake Validation

For every new BT:

1. **Parse check** — Valid JSON? Every node references valid actions from the action list above?
2. **Dry-run** — One short fight against a random pool member. Executes without crash? Takes ≥1 action per second?
3. **Degenerate check** — Run 3 micro-fights. If action logs are identical across all 3, reject.

If failed, retry once with error appended:
```
Your previous output failed validation:
{{ error_message }}

Please fix and return corrected tree as valid JSON.
```

Second failure → discard, slot stays open until next season.

---

## 14  Elo Seeding & New Entrant Rules

| Origin | Starting Elo |
|--------|-------------|
| Improve | Parent Elo − 50 |
| Mutate | 1200 (pool median) |
| Crossbreed | avg(Parent A, Parent B) − 25 |
| Manual injection | 1200 |

New entrants get **K=48** for first 10 matches (faster stabilization).

---

## 15  Stagnation Detection & Escape

Check every 3 seasons:
- **Elo compression:** max − min Elo < 80 → pool converging.
- **Archetype collapse:** >70% of BTs share >60% sub-trees.

### Prompt: Wildcard Injection

```
The chain-fist fighting AI pool has become stagnant. Dominant style:

{{ dominant_style_summary }}

Design a behavior tree from scratch using a FUNDAMENTALLY DIFFERENT approach.
Consider unexplored strategies:

- **Ceiling crawler**: Never touch ground; traverse via alternating ceiling anchors
- **Bait-and-punish**: Launch fist short, retract immediately, punish opponent's
  reaction with the other fist
- **Chain denial**: Focus on intercepting opponent fists (fist-vs-fist collision
  forces both to retract) to disarm before attacking
- **Momentum bomber**: Build max swing velocity from high anchor, release at peak
  speed for massive positional advantage
- **Ground pounder**: Never grapple; rely on grounded movement, spacing, and
  raw fist pressure with perfect timing

Do NOT reuse these overrepresented sub-trees:
{{ overrepresented_subtrees | join(", ") }}

Return the tree as valid JSON.
```

Wildcards enter at Elo 1200 with K=48 boost.

---

## 16  Domain-Specific Scouting Metrics

When analyzing battle logs, the LLM should pay attention to these chain-fist-specific metrics:

| Metric | What it reveals |
|--------|----------------|
| **Fist uptime** | % of ticks with at least one fist non-retracted. Low = passive. |
| **Dual-busy rate** | % of ticks with BOTH fists non-retracted. High = risky (no free fist to react). |
| **Grapple efficiency** | Hits landed during or immediately after swing ÷ total grapple anchors. |
| **Anchor height distribution** | Ceiling vs mid-air vs low anchors. Reveals grapple style. |
| **Whiff rate** | Fist extensions that retract without hitting. High = predictable or poorly aimed. |
| **Recovery time** | Average ticks from both-fists-retracting to next action. Long = exploitable gap. |
| **Damage-per-grapple** | Damage dealt per anchor-lock cycle. Measures grapple offense quality. |
| **Grounded vs airborne damage ratio** | Where does this fighter deal/take most damage? |

---

## 17  Full Season Flow

```
SEASON N
│
├─ A. Round-Robin Play ────────────────────── [headless Godot]
│     28 matches (pool of 8), battle logs emitted
│
├─ B. Battle Log Analysis ─────────────────── [LLM: scouting reports]
│     1 prompt per BT, chain-fist-specific analysis
│
├─ C. Elo Update ──────────────────────────── [deterministic]
│     standard Elo math, K=32
│
├─ D. Cull ─────────────────────────────────── [LLM: optional override]
│     remove 2–3 weakest / stalest
│     archetype diversity check
│
├─ E. Evolve ───────────────────────────────── [LLM: 2–3 prompts]
│     Improve / Mutate / Crossbreed
│     all prompts include action+condition reference
│
├─ F. Intake Validation ───────────────────── [headless dry-run]
│     parse, fight test, degenerate check
│
└─ → SEASON N+1
```

**LLM calls per season (pool of 8):** ~12–14

---

## 18  Tuning Knobs

| Parameter | Default | What it controls |
|-----------|---------|-----------------|
| `pool_target` | 8 | Steady-state population size |
| `pool_min` / `pool_max` | 5 / 10 | Hard bounds |
| `matches_per_season` | round-robin (28) | Fights before evolving |
| `match_duration_ticks` | 3600 | 60 seconds per match |
| `K_factor` | 32 | Elo volatility |
| `K_factor_newbie` | 48 | Elo volatility for first 10 matches |
| `cull_count` | 2–3 | Removed per season |
| `improve_weight` | 0.4 | Fraction of slots filled by Improve |
| `mutate_weight` | 0.3 | Fraction of slots filled by Mutate |
| `crossbreed_weight` | 0.3 | Fraction of slots filled by Crossbreed |
| `stagnation_check_interval` | 3 seasons | Convergence check frequency |
| `elo_spread_threshold` | 80 | Below this, pool is stagnant |
| `min_seasons_before_cull` | 2 | Grace period for new BTs |
| `fist_damage` | 12 | HP per hit |
| `max_chain_length` | 280 | Fist range in pixels |
| `grapple_pull_force` | 1800 | Pull strength when retracting anchored fist |
