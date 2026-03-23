# Simulation Constants Snapshot

**Date**: 2026-03-23
**Generations**: gen_000 through gen_005 (pre-tuning baseline)
**Purpose**: Preserve the "Season 1" ruleset before meta adjustments

---

## Fighter (`simulation/Fighter.cs`)

| Constant | Value | Notes |
|----------|-------|-------|
| Health | `100f` | Starting HP |
| BodyRadius | `18f` | Collision radius |

## Fist Mechanics (`simulation/Fist.cs`)

| Constant | Value | Notes |
|----------|-------|-------|
| MaxChainLength | `280f` | Max extension distance (px) |
| ExtendSpeed | `900f` | Punch launch speed (px/s) |
| RetractSpeed | `700f` | Chain retract speed (px/s) |
| FistRadius | `8f` | Fist collision radius (px) |

## Physics (`simulation/SimPhysics.cs`)

| Constant | Value | Notes |
|----------|-------|-------|
| Gravity | `980f` | px/s^2 |
| FixedDt | `1/60` | 60 FPS timestep |
| FistDamage | `12f` | Per-hit damage |
| MoveForce | `400f` | Ground horizontal force |
| JumpImpulse | `-420f` | Upward (Y-down coords) |
| AirMoveForce | `200f` | Air horizontal force |
| MaxHorizontalSpeed | `350f` | Speed cap (px/s) |
| GroundFriction | `0.85f` | Per-frame ground drag |
| AirFriction | `0.995f` | Per-frame air drag |
| GrapplePullForce | `1800f` | Retract pull force |
| SwingDamping | `0.995f` | Pendulum velocity decay |
| Knockback | `250f` | Hit knockback force (hardcoded in CheckFistHitBody) |
| GuardDeadZone | `3 * bodyRadius` | 54px, fists pass through at close range |

## Arena (`simulation/Arena.cs`)

| Constant | Value | Notes |
|----------|-------|-------|
| Width | `1200f` | Arena width (px) |
| Height | `680f` | Arena height (px) |
| WallThickness | `10f` | Boundary thickness |

## Match (`simulation/Match.cs`)

| Constant | Value | Notes |
|----------|-------|-------|
| MaxTicks | `3600` | 60s at 60fps |

## Tournament (`simulation/Tournament.cs`)

| Constant | Value | Notes |
|----------|-------|-------|
| BestOf | `7` | Matches per pairing |
| InitialElo | `1000f` | Starting rating |

## Derived Stats

| Metric | Value |
|--------|-------|
| Hits to KO | ~9 (100 / 12 = 8.3) |
| Punch travel time (full extend) | 0.31s (280 / 900) |
| Grapple close time (full chain) | ~0.16s at max pull |
| Jump apex height | ~90px (v^2 / 2g) |
| Arena aspect ratio | 1.76:1 |
| Typical match length | 5-10 seconds |
