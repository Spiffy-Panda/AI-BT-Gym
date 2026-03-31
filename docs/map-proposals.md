# Arena Map Proposals

## Current State

The arena is a **1500x680 px flat rectangle** with hard walls on all four sides. There are no obstacles, platforms, hazards, or dynamic elements. Fighters spawn at ~25% from each edge on the ground (Y=650). Fists can attach to walls and ceiling for grapple movement. All geometry is defined in code (`simulation/Arena.cs`, `SimPhysics.cs`), not scene files or tilemaps.

The simplicity of this layout means BT strategies currently reduce to horizontal spacing games: approach, retreat, time punches, and occasionally grapple off walls/ceiling. There's little reason to use verticality beyond evasion, and no positional territory worth controlling.

---

## Major Alterations

### 1. Center Platform

**What:** A solid rectangular platform (roughly 300x15 px) floating at ~60% height (Y ~400), centered horizontally. Fighters can stand on it, jump through it from below (one-way collision on top surface only), and attach fists to its underside.

**BT Strategy Impact:**
- Creates a **vertical positional advantage** — the fighter on the platform has a height edge for downward punches and can dodge ground-level attacks
- Introduces a **king-of-the-hill dynamic** where controlling the platform becomes a viable strategy
- Forces BTs to develop aerial approach/anti-air behaviors (jump-up attacks, pulling opponents off the platform via grapple)
- Grapple-to-underside opens a new movement pattern: hanging beneath the platform as a defensive position
- Spawn asymmetry becomes meaningful — one fighter might reach the platform first

**Implementation Complexity:** **Medium**
- Add a `Platform` class with position, size, and one-way collision logic to `SimPhysics.cs`
- Extend `Arena.cs` to hold a list of platforms
- Add ground detection for platform surfaces in `Fighter.IsOnGround`
- Add fist attachment logic for platform surfaces in `Fist.cs`
- Expose `"platform_x"`, `"platform_y"`, `"on_platform"` variables in `FighterBtContext.cs`
- Render the platform in `ArenaRenderer.cs`

---

### 2. Hazard Zones (Floor Damage Strips)

**What:** Two symmetric damage strips on the floor, each ~200 px wide, positioned at roughly 25% and 75% of the arena width (where fighters currently spawn). Any fighter standing on a damage strip takes 1 HP/second of passive damage. The strips are visually distinct (e.g., red-tinted floor segments). Spawn points shift to avoid starting directly on hazard zones.

**BT Strategy Impact:**
- **Punishes passive play** — fighters can't camp at their spawn location; the "safe" ground is now the center and far edges
- Creates **territorial pressure**: BTs must weigh the cost of retreating through a hazard zone vs. staying in contested center ground
- Encourages **aerial movement** — jumping over hazard zones or grappling across them becomes strategically valuable
- Knockback gains new meaning: pushing an opponent onto a hazard strip has cumulative value
- BTs evolve awareness of spatial zones, not just opponent distance

**Implementation Complexity:** **Low**
- Define hazard zone rectangles in `Arena.cs`
- Apply tick damage in `Match.Step()` when fighter position overlaps a hazard zone and fighter is grounded
- Shift spawn margins to avoid hazard strips
- Expose `"on_hazard"` boolean in `FighterBtContext.cs`
- Render colored floor strips in `ArenaRenderer.cs`

---

### 3. Destructible Center Wall

**What:** A vertical wall segment (~15x200 px) in the center of the arena, extending upward from the ground. The wall has 50 HP and absorbs fist hits (each hit does 7 damage to the wall instead of passing through). Once destroyed, the wall is gone for the rest of the match. Fists can attach to the wall while it stands.

**BT Strategy Impact:**
- **Splits the arena into two halves** at match start — fighters must go over or through it
- Creates a **resource decision**: spend fist attacks breaking the wall (giving up offensive pressure) or play around it with aerial movement
- While standing, the wall acts as **cover** — blocks line-of-sight fist attacks and forces diagonal/aerial approaches
- **Phase transition**: the match fundamentally changes once the wall breaks, requiring BTs that can adapt mid-match
- Grappling onto the wall creates a unique elevated position that disappears once destroyed
- Encourages BTs to develop distinct early-game (wall up) and late-game (wall down) strategies

**Implementation Complexity:** **High**
- New `DestructibleWall` class with HP, position, collision bounds
- Fist-vs-wall collision detection in `SimPhysics.cs` (block fist, deal damage to wall, retract fist)
- Fighter-vs-wall collision (prevent walking through)
- Wall destruction event and state tracking in `Match.cs`
- Expose `"wall_hp"`, `"wall_exists"` in `FighterBtContext.cs`
- Render wall with damage visual feedback in `ArenaRenderer.cs`
- Fist attachment to wall surfaces while wall exists

---

## Minor Additions

### 1. Corner Bumpers

**What:** Small diagonal collision surfaces (45-degree, ~60 px) in each of the four corners. When a fighter hits a bumper, their velocity reflects off the diagonal instead of zeroing out against the flat wall.

**BT Strategy Impact:**
- Corners become **escape routes** instead of death traps — a cornered fighter can bounce out at an angle
- Creates predictable deflection paths that BTs can learn to exploit or avoid
- Grapple-to-corner becomes a slingshot maneuver for fast repositioning

**Implementation Complexity:** **Low**
- Add diagonal collision check in `Arena.ClampToArena()` for corner regions
- Reflect velocity vector off the 45-degree surface normal
- Render small triangular fills in `ArenaRenderer.cs`

---

### 2. Health Pickup Spawn Point

**What:** A single health pickup (+10 HP, capped at 100) spawns at arena center (X=750, Y=650) every 10 seconds. It appears as a small circle on the ground. First fighter to touch it picks it up.

**BT Strategy Impact:**
- Creates a **contested objective** beyond just hitting the opponent
- Losing fighters have a comeback mechanic that rewards aggression toward center
- BTs must weigh risk of moving to center (exposed) vs. reward of healing
- Introduces timing awareness — knowing when the next pickup spawns

**Implementation Complexity:** **Low**
- Add `Pickup` struct with position, respawn timer, and active flag to `Match.cs`
- Check fighter-pickup overlap each tick; apply heal, reset timer
- Expose `"pickup_active"`, `"pickup_distance"` in `FighterBtContext.cs`
- Render a small circle/cross in `ArenaRenderer.cs`

---

### 3. Ceiling Height Variation

**What:** The ceiling is no longer flat. Instead, it dips lower at the center (Y=60 at center vs Y=10 at edges), forming a shallow inverted-V shape. The ceiling remains a valid fist-attachment surface.

**BT Strategy Impact:**
- **Center-ceiling grapples become shorter and faster** — a fighter in the center can reach the ceiling more quickly
- Edge-area fights have more vertical space for aerial maneuvers
- Creates asymmetric grapple dynamics depending on horizontal position
- BTs must account for position-dependent jump clearance

**Implementation Complexity:** **Medium**
- Change ceiling collision from flat line to piecewise linear or smooth curve in `Arena.cs`
- Update `ClampToArena()` to use position-dependent ceiling Y
- Update fist attachment ceiling check
- Update `ArenaRenderer.cs` ceiling drawing

---

### 4. Spawn Direction Indicator

**What:** A brief (1-second) directional arrow rendered at each fighter's spawn position pointing toward the opponent. Purely visual — does not affect simulation — but provides a reference for understanding replays and debugging BT initial behavior.

**BT Strategy Impact:**
- No direct gameplay impact (visual-only)
- Improves **observability** when reviewing evolved BT behavior in replays
- Helps identify whether BTs develop different opening strategies based on spawn side

**Implementation Complexity:** **Low**
- Add a transient arrow sprite/draw call in `FighterRenderer.cs` that fades over ~60 frames
- No simulation changes needed

---

### 5. Wall Friction Zones

**What:** The top 30% of each side wall (roughly Y=10 to Y=210) has a "sticky" property. When a fighter's body contacts this zone, their downward velocity is reduced by 50% (simulating wall friction / wall slide). The bottom 70% of walls behave normally.

**BT Strategy Impact:**
- Enables **wall-cling play** — fighters can slow their descent near the top of walls, staying airborne longer
- Creates a defensive option: grapple to upper wall, release, and slide down slowly while launching fists
- BTs that master wall friction gain superior aerial control and evasion
- Rewards vertical movement strategies without requiring platforms

**Implementation Complexity:** **Low**
- In `Arena.ClampToArena()`, check if fighter is touching a side wall in the friction zone
- Apply a velocity damping factor (e.g., `vel.Y *= 0.5f`) when in the friction region
- Optionally render a subtle texture/color difference on upper wall sections

---

### 6. Match Timer Pressure Ring

**What:** In the last 20% of match duration, the playable arena shrinks by 50 px on each side per 5-second interval (walls close in). The shrinking stops if only 100 px of width remains. Fist attachment points on the original walls still work; the new boundaries are force fields that push fighters inward.

**BT Strategy Impact:**
- **Prevents stalling** in late-game — defensive BTs can't run the clock by staying at max distance
- Creates **escalating tension**: strategies that work in the early match may fail when the arena tightens
- Forces close-quarters combat in the final moments, rewarding BTs with strong melee timing
- BTs must adapt their spacing and retreat distances as the match progresses
- Long-chain grapple strategies become less useful as the arena shrinks

**Implementation Complexity:** **Medium**
- Track effective arena bounds in `Match.cs`, shrinking on a timer
- Apply inward force (or hard clamp) at the shrinking boundary in `SimPhysics.cs`
- Expose `"arena_left"`, `"arena_right"` (current effective bounds) in `FighterBtContext.cs`
- Render the shrinking boundary as a distinct visual (e.g., pulsing line) in `ArenaRenderer.cs`
- Ensure fist attachment still works on the original (outer) walls
