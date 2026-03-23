// ─────────────────────────────────────────────────────────────────────────────
// Fist.cs — Fist state machine and chain data
// ─────────────────────────────────────────────────────────────────────────────
//
// Fists anchor mid-air when locked — no surface contact needed.
// Lock during Extending creates an immovable anchor at the fist's current
// position. The fighter can then swing (pendulum) or retract (pull toward).

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

    /// <summary>Lock the chain at current length. Creates mid-air anchor at fist position.</summary>
    public bool Lock()
    {
        if (ChainState != FistChainState.Extending) return false;
        ChainState = FistChainState.Locked;
        IsAttachedToWorld = true;
        AnchorPoint = Position;
        return true;
    }

    /// <summary>Begin retracting the fist.</summary>
    public bool Retract()
    {
        if (ChainState == FistChainState.Retracted || ChainState == FistChainState.Retracting) return false;
        ChainState = FistChainState.Retracting;
        return true;
    }

    /// <summary>Force retract (e.g. from collision). Also detaches.</summary>
    public void ForceRetract()
    {
        if (ChainState == FistChainState.Retracted) return;
        ChainState = FistChainState.Retracting;
        IsAttachedToWorld = false;
    }

    /// <summary>Detach fist from world anchor.</summary>
    public void Detach()
    {
        IsAttachedToWorld = false;
    }

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
                ChainLength += ExtendSpeed * dt;
                if (ChainLength > MaxChainLength)
                    ChainLength = MaxChainLength;

                Position = ownerPos + LaunchDirection * ChainLength;

                // Auto-retract at max length if not locked
                if (ChainLength >= MaxChainLength)
                    ChainState = FistChainState.Retracting;
                break;

            case FistChainState.Locked:
                if (IsAttachedToWorld)
                {
                    // Anchored: position stays at anchor, body swings (handled in physics)
                    Position = AnchorPoint;
                }
                else
                {
                    // Detached + locked: rigid arm, fist stays at fixed offset from owner
                    Position = ownerPos + LaunchDirection * ChainLength;
                }
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
