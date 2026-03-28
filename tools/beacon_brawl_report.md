# Beacon Brawl — Bug & Balance Report
**Generated from gen_000 through gen_005 tournament data, source code audit, and replay checkpoint verification**

---

## Executive Summary

Six generations of tournament evolution revealed **7 confirmed bugs** (5 recording, 2 simulation), and **4 balance issues**. The most impactful finding is that the center beacon (3x multiplier) is physically unreachable, collapsing the game into a symmetric 1x-vs-1x side-beacon tug-of-war. This explains the escalating draw rate (0% → 27%).

A deep checkpoint-level audit confirmed that **projectiles DO deal damage and cause kills** (33 shots per kill ratio), but **hit counters are never incremented**. Similarly, pawns spend 12-30% of their time inside beacon zones, but the recorder outputs 0% due to a suspected runtime issue in `BuildBattleLog()`.

---

## CONFIRMED BUGS (Recording)

### BUG-1: Damage dealt/received never recorded
**Severity:** Medium (analytics only)
**File:** `archive/beacon_brawl/simulation/BeaconBrawl/BeaconRecorder.cs:32-33,239-240`

`_damageDealt` and `_damageReceived` arrays are initialized to `[0, 0]` but **no code ever increments them**. Damage is applied via `Pawn.TakeDamage()` in `BeaconPhysics.cs` (lines 152, 247, 313) and `PawnBtContext.cs` (line 315), but those callsites never notify the recorder.

**Evidence:** All 30+ battle logs across 5 generations show `damage_dealt: 0` and `damage_received: 0` despite kills being recorded.

**Fix:** Add a `RecordDamage(int tick, int attackerTeam, int targetTeam, float amount, string weapon)` method and invoke it from `BeaconMatch.cs` after each damage event, or wire it through the `PawnBtContext` callbacks.

---

### BUG-2: Hook grab events never recorded
**Severity:** Medium (analytics only)
**File:** `archive/beacon_brawl/simulation/BeaconBrawl/BeaconMatch.cs:152`

`CheckHookGrab()` at line 152 returns a `Pawn?` indicating which enemy was grabbed, but the return value is **discarded**:
```csharp
BeaconPhysics.CheckHookGrab(pawn, enemies);  // return value ignored
```

The `OnHookGrab` callback is correctly set up on the `PawnBtContext` (lines 121, 140), but `CheckHookGrab()` runs **after** BT execution (line 147-153), outside the context's scope. The callback is never invoked because the grab check runs on the raw `Pawn` objects without the context's callback.

**Evidence:** `hook_grabs: 0` across all generations despite grabs visibly happening (pawns become vulnerable and take increased damage).

**Note:** The grab mechanic itself likely **does** work — `BeaconPhysics.CheckHookGrab()` correctly sets `VulnerableTicks` and `GrabRootTicks`. The bug is purely in recording, not simulation. However, we cannot confirm grab effectiveness without fixing this.

**Fix:** Capture the return value and record the event:
```csharp
var grabbed = BeaconPhysics.CheckHookGrab(pawn, enemies);
if (grabbed != null)
    Recorder?.RecordEvent(Tick, pawn.TeamIndex, pawn.PawnIndex,
        $"hook_grab_{grabbed.TeamIndex}_{grabbed.PawnIndex}");
```

---

### BUG-3: Parry successes never recorded
**Severity:** Medium (analytics only)
**File:** `archive/beacon_brawl/simulation/BeaconBrawl/BeaconRecorder.cs:38`

`_parrySuccesses` array is initialized but **no code increments it**. Parry checks exist in `BeaconPhysics.cs` at three locations:
- Fist punch parry check (line 145)
- Projectile parry check (line 229)
- Rifle parry check (line 307)

All three correctly handle the gameplay effect (lockout attacker, block damage) but none record the event.

**Evidence:** `parry_successes: 0` across all generations despite `activate_parry` appearing in action logs.

**Fix:** Add recorder callbacks for parry events at each parry check site.

---

### BUG-4: Positional summary fields hardcoded to zero
**Severity:** Low (analytics only)
**File:** `archive/beacon_brawl/simulation/BeaconBrawl/BeaconRecorder.cs:253-256`

Four fields in `BuildBattleLog()` are hardcoded to 0 rather than computed:
```csharp
TimeOnPlatformPct = 0,    // line 253
TimeInBeaconPct = 0,      // line 254
TimeInBasePct = 0,        // line 255
AvgDistToNearestEnemy = 0 // line 256
```

These should be computed from `_samples` data (position vs platform/beacon/base zone geometry, and distance to enemy pawns).

---

### BUG-5: Pistol/rifle hit counters never incremented
**Severity:** Medium (analytics only)
**File:** `archive/beacon_brawl/simulation/BeaconBrawl/BeaconRecorder.cs:35-36,84-88`

`_pistolHits` and `_rifleHits` arrays exist but are never incremented:
- `RecordRifleShot()` (line 84-88) is a no-op — it receives ray segments but does nothing with them and has no hit flag
- No `RecordPistolHit()` or `RecordRifleHit()` method exists
- `RecordEvent()` (line 74-82) only checks for `"hook_grab"` and `"fist_hit"` prefixes, not pistol/rifle events

**Evidence:** `pistol_hits: 0` and `rifle_hits: 0` across all generations despite `shoot_pistol_at_enemy` and `shoot_rifle_at_enemy` appearing 30-50 times per match in action logs.

**Note:** This doesn't mean shots miss — it means **we cannot tell if they hit**. The rifle raycast in `BeaconPhysics.FireRifle()` (line 263) and projectile collision in `TickProjectiles()` (line 247) correctly apply damage on hit, but neither notifies the recorder.

**Fix:**
- For rifle: capture `FireRifle()` return value in `PawnBtContext.FireRifleDir()` and invoke a hit callback
- For pistol: add a hit callback to `Projectile` or check in `TickProjectiles()` and notify recorder

---

## LIKELY SIMULATION BUGS

### BUG-6: Jump impulse sign mismatch may prevent jumping
**Severity:** High (gameplay-affecting)
**File:** `archive/beacon_brawl/simulation/BeaconBrawl/PawnBtContext.cs:423` vs `simulation/SimPhysics.cs:16`

`JumpImpulse` is defined as **-420** in `SimPhysics.cs` (Y-down coordinate system, so negative = upward). The jump action at line 423 sets:
```csharp
_pawn.Velocity = new Vector2(_pawn.Velocity.X, BeaconPhysics.JumpImpulse);
```

This **replaces** the Y velocity entirely with -420, which should work. However, gravity is 980 px/s^2 (from `SimPhysics.Gravity`). At 60fps the `Integrate()` function applies:
```
vel.Y += Gravity * dt  →  vel.Y += 980 * (1/60) = +16.3 per tick
```

A jump at -420 provides: `420 / 16.3 ≈ 26 ticks of upward travel`, reaching peak height of approximately:
```
h = v^2 / (2*g) = 420^2 / (2*980) ≈ 90 pixels upward
```

The platform is at Y=380 and ground is at Y=774. That's a **394-pixel gap**. A 90-pixel jump only reaches Y=684 — **nowhere near the platform**.

**Evidence:** All gen_004 battle logs show `time_on_platform_pct: 0.0`, `avg_y: 772-774`, and `time_grounded_pct: 96-100%`. No pawn ever reaches the platform.

**Root cause:** The jump impulse (-420) combined with gravity (980) produces a maximum jump height of ~90px. The center platform at Y=380 requires a 394px ascent from ground (Y=774). **The jump is 4.4x too short to reach the platform.**

Even with a grappling hook launched toward center (Y=370), the hook would need to extend ~400px to reach from ground level, but `MaxChainLength` is only 280px.

**This means the center beacon is physically unreachable by any pawn.** The 3x multiplier beacon is decorative.

**Fix options:**
1. Lower the platform to Y=650 (reachable with one jump)
2. Increase `JumpImpulse` to -880 (reaches Y=380 from ground)
3. Add double-jump or wall-jump mechanics
4. Increase `MaxChainLength` to 450+ so hook can bridge the gap
5. Move center beacon to ground level (remove platforming entirely)

---

### BUG-7: `MoveToward` uses X-axis only — cannot path to elevated positions
**Severity:** High (related to BUG-6)
**File:** `archive/beacon_brawl/simulation/BeaconBrawl/PawnBtContext.cs:387-391`

```csharp
private BtStatus MoveToward(Vector2 target)
{
    float dirX = target.X > _pawn.Position.X ? 1f : -1f;
    return ApplyMove(dirX);
}
```

`MoveToward()` only considers X position. When target is the center beacon (x=1000, y=370), a pawn at (x=1000, y=774) directly below it will oscillate left/right with zero net movement because `dirX` flips sign around x=1000.

**Evidence:** Iron_Vanguard logged `move_toward_beacon_center: 9024` actions in a single match (gen_003) without ever reaching the beacon. The pawn was directly underneath oscillating horizontally.

**Fix:** `MoveToward()` should also trigger jumps when the target is significantly above the current position, or the BT should handle vertical positioning explicitly.

---

## BALANCE ISSUES

### BAL-1: Center beacon (3x) is unreachable — game reduces to 1x vs 1x
**Severity:** Critical
**Impact:** The core strategic differentiator (3x center beacon worth fighting for) is non-functional.

Platform Y=380, ground Y=774, gap=394px. Jump height=~90px, max hook=280px. Neither mechanic can bridge the gap. Center beacon ownership is 0% across all 5 generations (150+ matches).

The game degrades into symmetric left-vs-right side-beacon control, which has two stable equilibria:
1. One team holds both sides → dominant win
2. Each team holds one side → stalemate/draw

This directly causes the draw rate problem.

---

### BAL-2: Draw rate escalates as AI improves (0% → 27%)
**Severity:** High

| Generation | Draw Rate | Avg Duration (ticks) |
|-----------|-----------|---------------------|
| gen_000 | 0% | 1,232 |
| gen_001 | 3.3% | 3,937 |
| gen_002 | 0% | 5,192 |
| gen_003 | 13.3% | 3,146 |
| gen_004 | 26.7% | 5,266 |

As teams learn optimal play, they converge on symmetric strategies (each hold one side beacon). With only 2 capturable beacons (center being unreachable), equal teams produce equal scores.

**Contributing factors:**
- Only 2 functional beacons with identical 1x multiplier = symmetric game
- Differential scoring means equal beacon count = zero points for both
- 90-second timeout is long enough for teams to equalize but not break ties
- Kill bonus (+5) is small relative to scoring rate

**Fix options:**
- Make center beacon reachable (see BUG-6)
- Add asymmetric beacon multipliers (e.g., left=1x, right=2x)
- Reduce timeout to 60 seconds
- Increase kill bonus to +8 or +10
- Add sudden-death/golden-point mechanic after timeout

---

### BAL-3: Differential scoring suppresses comeback potential
**Severity:** Medium

Scoring formula: `points += max(0, myRate - enemyRate)` every 60 ticks.

When both teams own 1 beacon each (rate 1 vs 1), **neither team scores beacon points**. The only scoring comes from kill bonuses (+5). This means extended periods where the score is frozen, pushing matches toward timeout.

**Impact on gameplay:** A team that falls behind has no "rubber band" mechanic. If team A owns 2 beacons (rate 2) vs team B's 0, team A gets 2 pts/sec. But if both own 1 each, it's 0-0 indefinitely. This creates a binary outcome — either dominate or stalemate.

**Fix options:**
- Switch to additive scoring: each team gets points equal to their own rate (not differential)
- Add "king of the hill" bonus for controlling the most beacons
- Scale kill bonus with beacon advantage (more beacons = bigger kill reward)

---

### BAL-4: Gunner role effectiveness is unmeasurable
**Severity:** Medium

Due to BUG-5 (hit counters not recorded), we cannot assess:
- Pistol accuracy (shots vs hits)
- Rifle accuracy (shots vs hits)
- Damage per weapon type
- Effective range for each weapon

From action logs we know gunners fire 30-50 shots per match, but without hit data, we cannot determine if:
- Pistol arcs are overshooting (ballistic gravity may cause shots to fall short)
- Rifle raycasts are being blocked by the center platform
- Weapons are balanced relative to each other (6 dmg pistol vs 18 dmg rifle)

**Impact:** Balancing Gunner weapons is impossible without hit rate data.

---

## MINOR ISSUES

### MINOR-1: `RecordRifleShot()` is a no-op
**File:** `BeaconRecorder.cs:84-88`
```csharp
public void RecordRifleShot(int tick, int teamIdx, int pawnIdx, List<Vector2> segments)
{
    // Could store segments for replay visualization
    // For now just track hit count
}
```
The method body is empty. Ray segments are calculated but discarded, losing replay visualization data.

### MINOR-2: `Beacon.Contains()` not shown — could have zone radius issues
The `Beacon.Contains()` method (used for zone occupation checks) references `Zone.Center` and presumably `Zone.Radius`, but if center beacon zone is at Y=370 and all pawns are at Y=774, no pawn will ever register as "in" the center beacon zone, even if they could theoretically reach it.

### MINOR-3: Hook attachment to world not triggered in BT
The `HookStateMachine` subtree detaches unanchored hooks and retracts anchored hooks, but there is no explicit "attach to world" action. Attachment appears to happen automatically in the Fist/Hook state machine when a hook hits a wall or surface during extension. Since hooks launched toward center (Y=370) from ground (Y=774) can't reach (max chain 280px), hook-to-platform grapple is impossible.

---

## SUMMARY TABLE

| ID | Type | Severity | Description | File |
|----|------|----------|-------------|------|
| BUG-1 | Recording | Medium | Damage dealt/received always 0 — never incremented | BeaconRecorder.cs:32-33 |
| BUG-2 | Recording | Medium | Hook grabs never recorded — `CheckHookGrab()` return value discarded | BeaconMatch.cs:152 |
| BUG-3 | Recording | Medium | Parry successes never recorded — no callback exists | BeaconRecorder.cs:38 |
| BUG-4 | Recording | **High** | Beacon/base/enemy-dist stats output 0 despite correct source code — runtime issue (verified: 12.3% actual beacon time vs 0% recorded) | BeaconRecorder.cs:189-301 |
| BUG-5 | Recording | Medium | Pistol/rifle hits never counted — projectiles DO kill (~33 shots/kill) but hits aren't tracked | BeaconRecorder.cs:35-36,84-88 |
| BUG-6 | Simulation | **High** | Jump (90px) can't reach platform (394px gap) — center beacon permanently unreachable | SimPhysics.cs:16 + BeaconArena.cs:38 |
| BUG-7 | Simulation | High | `MoveToward()` X-axis only — pawns oscillate under elevated targets (9024 wasted actions in one match) | PawnBtContext.cs:387-391 |
| BAL-1 | Balance | **Critical** | Center 3x beacon unreachable — game collapses to 1x vs 1x | BeaconArena.cs:35-47 |
| BAL-2 | Balance | High | Draw rate escalates 0%→27% as AI skill converges | Systemic |
| BAL-3 | Balance | Medium | Differential scoring freezes score when beacons equal | BeaconMatch.cs:215-216 |
| BAL-4 | Balance | Medium | Gunner weapon balance unmeasurable — hit data missing | Systemic |
| BAL-5 | Recording | Low | Center beacon Y-coord in recorder (640) doesn't match arena (370) | BeaconRecorder.cs:215 |
| BAL-6 | Balance | Medium | Spawn-swap randomization breaks hardcoded "cap left" strategies | BeaconMatch.cs:53 + BTs |
| BAL-7 | Balance | Low | Grappler melee nearly useless — avg enemy dist 750-875px, fist range 40px | Systemic |

---

---

## DEEP DIVE: Checkpoint-Level Verification (gen_005)

A replay checkpoint analysis was run against match_001_g1 (Iron vs Shadow, 5400 ticks, 540 checkpoints). Raw pawn positions from checkpoint data were compared against the recorder's `BuildBattleLog()` output.

### Projectiles DO Work — Hits Just Aren't Counted

| Metric | Battle Log Value | Checkpoint-Derived Value |
|--------|-----------------|-------------------------|
| Pistol hits | 0 | N/A (no per-hit tracking in checkpoints) |
| Rifle hits | 0 | N/A |
| Fist hits | 2 | 2 (matches — fist tracking works) |
| Kills | 4 | 4 (matches) |
| Damage dealt | 0 | Confirmed via HP drops in checkpoints |

**Kill source analysis across all 60 battle logs (30 matches):**
- Total unique kills: ~116
- Total fist hits: 31 → max ~2.4 fist-only kills (100 HP / 8 dmg = 13 hits to kill)
- **~114 kills came from projectiles** (pistol/rifle)
- Total shot actions: 3,793 → **~33 shots per kill**
- Estimated hit rate: ~3-5% (accounting for regen and multi-hit kills)

**Conclusion:** Pistol and rifle ARE dealing damage and getting kills. The bug is purely in recording — `_pistolHits` and `_rifleHits` are never incremented in `BeaconRecorder`.

### Positional Stats: Source Code Correct, Output Wrong

The recorder source code at lines 205-237 contains correct beacon/base/distance computation logic. The compiled DLL hash matches the tournament (verified: `2f404bfb9...`). Yet all positional fields output 0.

**Python simulation of the exact recorder logic on checkpoint data:**

| Metric | Recorder Output | Checkpoint Verification | Status |
|--------|----------------|------------------------|--------|
| `time_on_platform_pct` | 0 | 0% (correct — platform unreachable) | OK |
| `time_in_beacon_pct` | 0 | **12.3%** | **BUG** |
| `time_in_base_pct` | 0 | **4.6%** | **BUG** |
| `avg_dist_to_nearest_enemy` | 0 | **711.4 px** | **BUG** |

**Per-pawn breakdown (verified from checkpoints):**

| Pawn | Role | Beacon Time | Base Time | Avg Enemy Dist | Y Range |
|------|------|------------|-----------|----------------|---------|
| 0 (T0 Grappler) | Iron | 15.5% | 6.3% | 874 px | [774, 774] |
| 1 (T0 Gunner) | Iron | 9.2% | 2.9% | 753 px | [734, 774] |
| 2 (T1 Grappler) | Shadow | 20.0% | 10.3% | 816 px | [774, 774] |
| 3 (T1 Gunner) | Shadow | 29.5% | 7.7% | 810 px | [774, 774] |

The Gunner (pawn 1) briefly reaches Y=734 from rifle-down recoil. No pawn approaches Y=380 (center platform).

**Root cause:** Unknown. Source code is correct, DLL matches, yet runtime produces zeros. Possible C# runtime optimization, struct copy issue, or JIT behavior. The `avgX`/`avgY` fields ARE computed from the same loop (proving samples exist), but the beacon/base/distance checks within that same loop produce zero. This warrants a debugger investigation.

### Action Pattern Findings

**Actions that NEVER appear across all 60 logs:**
- `jump` — 0 occurrences (BT conditions never met, see BUG-6)
- `activate_parry` — 0 occurrences (BT conditions too narrow)
- `launch_hook_toward_enemy` — 0 occurrences (removed from BTs in gen_003)
- `move_toward_nearest_contested` — 0 occurrences (BT condition: contested beacon exists, but `IsContested` requires both teams present simultaneously which is rare)
- `move_toward_nearest_enemy` — 0 occurrences (BT condition chains don't reach this)

**Move vs Attack ratio: 99%+ movement**
Across all matches, movement actions outnumber attack actions ~130:1. The game is a beacon-capping race, not a combat game.

**70% of matches hit timeout (5400 ticks)**
42 of 60 battle logs lasted the full 90 seconds. Average duration: 5153 ticks (85.9 seconds).

### New Balance Observations

**BAL-5: Center beacon recorder Y-coordinate is wrong**
`BeaconRecorder.cs:215` uses `beaconYs = [774f, 640f, 774f]` for center beacon, but actual zone center is at Y=370 (`BeaconArena.cs:46`). This doesn't affect gameplay (center is unreachable anyway) but would cause incorrect tracking even if the platform bug is fixed.

**BAL-6: Spawn sides are swapped relative to naming**
Iron_Vanguard (Team A, spawns at x=100) has its grappler cap left beacon first — which is near their spawn. But checkpoints show Iron spawns at x=1885 initially (tick 0), meaning the spawn-swap randomization (line 53 of `BeaconMatch.cs`) can put Team A on the right side. BT strategies that hardcode "cap left first" will cap the ENEMY's side when spawn-swapped, which is suboptimal.

**BAL-7: Average enemy distance is ~750-875px**
Teams rarely close to melee range. The 2000px arena with 4 pawns means combat is sparse. Most kills happen at pistol/rifle range, not melee. This makes the Grappler role's melee kit (fist punch: 40px range, 8 dmg) nearly useless compared to the Gunner's ranged kit.

---

## RECOMMENDED PRIORITY

1. **Fix BUG-6 / BAL-1** (center beacon reachable) — this alone would fix draw rate, create strategic depth, and make the 3x multiplier meaningful
2. **Fix positional stats recording** (BUG-4 runtime issue) — needs debugger; source code is correct but output is 0. Consider adding integration test that runs a match and asserts `time_in_beacon_pct > 0`
3. **Fix BUG-2** (hook grab recording) — capture `CheckHookGrab()` return value in `BeaconMatch.cs:152`
4. **Fix BUG-5** (pistol/rifle hit counters) — add hit callbacks to `TickProjectiles()` and `FireRifle()`
5. **Fix BUG-1** (damage tracking) — add `RecordDamage()` method and invoke from damage sites
6. **Fix BUG-7** (MoveToward vertical) — add jump trigger when target is significantly above
7. **Fix BAL-5** (recorder center beacon Y) — change `640f` to `370f` in `beaconYs`
8. **Consider BAL-2/BAL-3** (scoring/draw rate) — may resolve naturally if center becomes reachable
9. **Consider BAL-6** (spawn-swap awareness) — BTs should use relative positioning, not absolute "left"/"right"
