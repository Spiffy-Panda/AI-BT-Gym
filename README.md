# AI-BT-Gym

An evolutionary behavior tree gym built in Godot. LLM-generated behavior trees compete in a symmetric 2D fighting game, evolving through iterative evaluation and selection.

## Concept

Agents control fighters in a side-on fighting game where the primary mechanic is launching fists on chains. Fists can extend, lock, retract, and attach/detach from the world — enabling punching, wall-jumping, swinging, and hammering. Behavior trees define each agent's strategy and are evolved over generations.

## Architecture

- **Headless Simulation (C#)** — Core game logic and physics for fast batch evaluation of behavior trees
- **Godot Renderer** — Visual playback of matches for cinematic replays and debugging
- **Web Server (C#)** — Data management, visualization dashboard, and experiment tracking

## Combat Mechanics

Each fighter has two fists on chains with the following states:

| State | Description |
|-------|-------------|
| **Retracted** | Fist is at the player, ready to launch |
| **Extending** | Fist is traveling outward on the chain |
| **Locked** | Chain length is fixed (enables swinging if anchored, hammering if free) |
| **Retracting** | Fist is returning to the player |

Fists can be **attached** or **detached** from the world:

- **Attached + Locked** — Anchor point; player swings from it
- **Detached + Locked** — Rigid arm; enables hammer strikes
- **Attached + Retracting** — Pull yourself toward the anchor (wall jump / grapple)
- **Fist collision** — Forces immediate retraction back to the player

## Getting Started

*TODO: Setup instructions once project scaffolding is in place.*

## License

*TBD*
