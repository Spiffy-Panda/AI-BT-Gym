# Design Document — AI-BT-Gym

## 1. Overview

AI-BT-Gym is an evolutionary training environment where LLM-generated behavior trees compete in a symmetric 2D chain-fist fighting game. The system evaluates populations of BTs through headless simulation, selects top performers, and iterates — producing increasingly sophisticated combat strategies.

## 2. Game Design

### 2.1 Arena

- 2D side-on view with walls, floor, and ceiling
- Symmetric layout — no positional advantage
- Anchor surfaces on walls and ceiling for fist attachment

### 2.2 Fighter

Each fighter has:
- A body with position, velocity, and health
- Two fists, each on a chain

### 2.3 Fist / Chain Mechanics

Each fist operates as an independent state machine:

```
        ┌──────────┐
        │ Retracted │◄──────────────────────┐
        └────┬─────┘                        │
             │ launch                       │ collision / retract
             ▼                              │
        ┌──────────┐                        │
        │ Extending │───────────────────────┤
        └────┬─────┘                        │
             │ lock                         │
             ▼                              │
        ┌──────────┐                        │
        │  Locked   │───────────────────────┤
        └────┬─────┘                        │
             │ retract                      │
             ▼                              │
        ┌────────────┐                      │
        │ Retracting  │─────────────────────┘
        └─────────────┘
```

**World attachment** is orthogonal to chain state:
- A fist can attach to a surface when it contacts one (while extending or locked)
- A fist can detach at any time
- Attachment persists through state transitions until explicitly detached or the fist retracts fully

**Derived actions from state combinations:**

| Action | How |
|--------|-----|
| Punch | Launch fist → extends toward opponent → retract |
| Grapple / Wall Jump | Launch fist at surface → attach → retract (pulls player toward anchor) |
| Swing | Launch fist at surface → attach → lock (player swings on chain) |
| Hammer | Launch fist → lock while detached (rigid arm flail / slam) |
| Block / Parry | Position locked fist defensively (fist collision forces opponent retract) |

**Fist collision rule:** When two fists collide, both are forced into immediate retraction. This creates risk/reward — overextending leaves you vulnerable.

### 2.4 Scoring

- Damage dealt to opponent
- Health remaining
- Match timeout → most health wins
- Potential style bonuses (variety of techniques used) for evolutionary pressure toward interesting strategies

## 3. Behavior Tree System

### 3.1 Node Types

**Composite:**
- Sequence — execute children left-to-right, fail on first failure
- Selector (Fallback) — execute children left-to-right, succeed on first success
- Parallel — execute children concurrently, configurable success/fail policy

**Decorator:**
- Inverter — flip child result
- Repeater — repeat child N times or until fail
- Cooldown — prevent re-execution for N ticks
- Condition Gate — only run child if condition is true

**Leaf (Action):**
- LaunchFist(hand, direction)
- RetractFist(hand)
- LockFist(hand)
- AttachFist(hand)
- DetachFist(hand)
- MoveToward(target)
- MoveAway(target)

**Leaf (Condition):**
- IsFistRetracted(hand)
- IsFistAttached(hand)
- IsOpponentInRange(range)
- IsOpponentFistExtending(hand)
- IsHealthBelow(threshold)
- IsNearSurface(direction)

### 3.2 BT Representation

Behavior trees are serialized as JSON for:
- LLM generation and mutation
- Storage and versioning
- Headless evaluation without Godot dependency

### 3.3 Evolution Loop

```
┌─────────────────────────────────────────┐
│  1. Generate initial BT population      │
│     (LLM or random)                     │
├─────────────────────────────────────────┤
│  2. Run tournament (headless sim)       │
│     - Round-robin or bracket            │
│     - Multiple matches per pairing      │
├─────────────────────────────────────────┤
│  3. Score and rank                      │
├─────────────────────────────────────────┤
│  4. Select top performers               │
├─────────────────────────────────────────┤
│  5. LLM mutates / crosses over BTs      │
│     - Prompt includes parent BTs        │
│     - Prompt includes match statistics  │
│     - LLM produces child BTs            │
├─────────────────────────────────────────┤
│  6. Repeat from step 2                  │
└─────────────────────────────────────────┘
```

## 4. Architecture

### 4.1 Headless Simulation (C#)

- Pure C# game loop — no Godot dependency
- Deterministic physics (fixed timestep)
- Runs N matches in parallel for fast evaluation
- Outputs match results + replay data (state snapshots per tick)

### 4.2 Godot Renderer

- Consumes replay data to visualize matches
- Camera, animations, particle effects for cinematic playback
- Debug overlays: BT execution state, fist state, hitboxes
- Not required for training — purely for observation

### 4.3 Web Server (C#)

- ASP.NET or similar lightweight C# web framework
- REST API for:
  - Submitting BT populations
  - Triggering evaluation runs
  - Querying results and leaderboards
- Dashboard UI:
  - Generation-over-generation fitness graphs
  - BT visualizer (tree rendering)
  - Match replay selector (triggers Godot renderer)
  - Population diversity metrics

## 5. Project Structure (Planned)

```
AI-BT-Gym/
├── src/
│   ├── Simulation/          # Headless C# game logic
│   │   ├── Fighter.cs
│   │   ├── Fist.cs
│   │   ├── Arena.cs
│   │   ├── Match.cs
│   │   └── Physics.cs
│   ├── BehaviorTree/        # BT runtime and serialization
│   │   ├── Nodes/
│   │   ├── BTreeRunner.cs
│   │   └── BTreeSerializer.cs
│   ├── Evolution/           # Population management and LLM integration
│   │   ├── Population.cs
│   │   ├── Tournament.cs
│   │   └── LLMMutator.cs
│   └── WebServer/           # Data management and dashboard
│       ├── Controllers/
│       ├── Services/
│       └── wwwroot/
├── godot/                   # Godot project for rendering
│   ├── Scenes/
│   ├── Scripts/
│   └── project.godot
├── DESIGN.md
├── README.md
└── .gitignore
```

## 6. Open Questions

- **Physics fidelity:** How simplified can headless physics be while still producing meaningful strategies?
- **LLM provider:** Which LLM to use for BT generation/mutation? (Cost vs quality tradeoff at scale)
- **BT complexity cap:** Maximum tree depth/node count to keep evaluation tractable?
- **Fitness function tuning:** How to balance damage, survival, and style diversity?
- **Replay format:** Minimal state snapshot format that supports both Godot rendering and web visualization?
