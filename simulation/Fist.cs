// ─────────────────────────────────────────────────────────────────────────────
// Fist.cs — Fist state machine and chain data
// ─────────────────────────────────────────────────────────────────────────────

using System;
using Godot;

namespace AiBtGym.Simulation;

public enum FistChainState { Retracted, Extending, Locked, Retracting }

public class Fist
{
    public FistChainState ChainState { get; private set; } = FistChainState.Retracted;
    public bool IsAttachedToWorld { get; private set; }
    public Vector2 Position { get; set; }
    public Vector2 AnchorPoint { get; set; }
    public Vector2 LaunchDirection { get; set; }
    public float ChainLength { get; set; }

    // Config
    public float MaxChainLength = 280f;
    public float ExtendSpeed = 900f;
    public float RetractSpeed = 700f;
    public float FistRadius = 8f;

    /// <summary>Launch fist from retracted state in a given direction.</summary>
    public bool Launch(Vector2 direction)
    {
        if (ChainState != FistChainState.Retracted) return false;
        LaunchDirection = direction.Normalized();
        ChainState = FistChainState.Extending;
        ChainLength = 0f;
        IsAttachedToWorld = false;
        return true;
    }

    /// <summary>Lock the chain at current length.</summary>
    public bool Lock()
    {
        if (ChainState != FistChainState.Extending) return false;
        ChainState = FistChainState.Locked;
        return true;
    }

    /// <summary>Begin retracting the fist.</summary>
    public bool Retract()
    {
        if (ChainState == FistChainState.Retracted || ChainState == FistChainState.Retracting) return false;
        ChainState = FistChainState.Retracting;
        return true;
    }

    /// <summary>Force retract (e.g. from collision).</summary>
    public void ForceRetract()
    {
        if (ChainState == FistChainState.Retracted) return;
        ChainState = FistChainState.Retracting;
    }

    /// <summary>Attach fist to a world surface point.</summary>
    public bool Attach(Vector2 surfacePoint)
    {
        if (ChainState == FistChainState.Retracted || ChainState == FistChainState.Retracting) return false;
        IsAttachedToWorld = true;
        AnchorPoint = surfacePoint;
        Position = surfacePoint;
        return true;
    }

    /// <summary>Detach fist from world.</summary>
    public void Detach()
    {
        IsAttachedToWorld = false;
    }

    /// <summary>
    /// Optional surface check callback set by the simulation.
    /// Called during Tick when extending, after position update but before auto-retract.
    /// Returns (attached, surfacePoint).
    /// </summary>
    public Func<Vector2, (bool hit, Vector2 point)>? SurfaceCheck { get; set; }

    /// <summary>Advance fist state each tick.</summary>
    public void Tick(float dt, Vector2 ownerPos)
    {
        switch (ChainState)
        {
            case FistChainState.Retracted:
                Position = ownerPos;
                ChainLength = 0f;
                IsAttachedToWorld = false;
                break;

            case FistChainState.Extending:
                if (!IsAttachedToWorld)
                {
                    ChainLength += ExtendSpeed * dt;
                    if (ChainLength > MaxChainLength)
                        ChainLength = MaxChainLength;

                    Position = ownerPos + LaunchDirection * ChainLength;

                    // Check for surface attach before auto-retract
                    if (SurfaceCheck != null)
                    {
                        var (hit, point) = SurfaceCheck(Position);
                        if (hit)
                        {
                            Attach(point);
                            // Recalculate chain length to match actual distance to anchor
                            ChainLength = ownerPos.DistanceTo(point);
                            break;
                        }
                    }

                    // Auto-retract at max length if not attached
                    if (ChainLength >= MaxChainLength)
                        ChainState = FistChainState.Retracting;
                }
                break;

            case FistChainState.Locked:
                if (!IsAttachedToWorld)
                {
                    // Detached + locked: fist stays at fixed offset from owner
                    Position = ownerPos + LaunchDirection * ChainLength;
                }
                // Attached + locked: position stays at anchor, body swings (handled in physics)
                break;

            case FistChainState.Retracting:
                ChainLength -= RetractSpeed * dt;
                if (ChainLength <= 0f)
                {
                    ChainLength = 0f;
                    ChainState = FistChainState.Retracted;
                    IsAttachedToWorld = false;
                    Position = ownerPos;
                }
                else if (IsAttachedToWorld)
                {
                    // Position stays at anchor while retracting — body gets pulled
                    Position = AnchorPoint;
                }
                else
                {
                    Position = ownerPos + LaunchDirection * ChainLength;
                }
                break;
        }
    }
}
