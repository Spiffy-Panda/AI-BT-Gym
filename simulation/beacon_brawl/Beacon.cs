// ─────────────────────────────────────────────────────────────────────────────
// Beacon.cs — Capture zone state and scoring logic
// ─────────────────────────────────────────────────────────────────────────────

using Godot;

namespace AiBtGym.Simulation.BeaconBrawl;

public class Beacon
{
    public BeaconZone Zone { get; }

    /// <summary>0 = neutral, 1 = team A, 2 = team B.</summary>
    public int OwnerTeam { get; set; }

    /// <summary>Capture progress toward CapturingTeam (0 to CaptureThreshold).</summary>
    public int CaptureProgress { get; set; }

    /// <summary>Which team is currently accumulating capture progress (1 or 2, 0 if none).</summary>
    public int CapturingTeam { get; set; }

    /// <summary>True if both teams have pawns inside this tick.</summary>
    public bool IsContested { get; set; }

    public const int CaptureThreshold = 90; // 1.5 seconds at 60fps
    public const int ScoreInterval = 60;    // scoring happens every 60 ticks (1 second)

    public Beacon(BeaconZone zone)
    {
        Zone = zone;
    }

    /// <summary>
    /// Advance beacon capture state (no scoring — that's handled by BeaconMatch differential scoring).
    /// teamACounts/teamBCounts = number of pawns from each team inside the zone.
    /// </summary>
    public void Tick(int teamACounts, int teamBCounts)
    {
        bool aPresent = teamACounts > 0;
        bool bPresent = teamBCounts > 0;
        IsContested = aPresent && bPresent;

        // Capture logic
        if (IsContested)
        {
            // Both present: frozen, no capture progress
        }
        else if (aPresent && !bPresent)
        {
            AdvanceCapture(1);
        }
        else if (bPresent && !aPresent)
        {
            AdvanceCapture(2);
        }
        else
        {
            // Nobody present: capture progress decays slowly
            if (CaptureProgress > 0 && OwnerTeam == 0)
            {
                CaptureProgress--;
                if (CaptureProgress == 0) CapturingTeam = 0;
            }
        }
    }

    private void AdvanceCapture(int team)
    {
        if (OwnerTeam == team)
        {
            // Already own it — no further capture needed
            return;
        }

        if (CapturingTeam == team)
        {
            // Continue capturing
            CaptureProgress++;
            if (CaptureProgress >= CaptureThreshold)
            {
                OwnerTeam = team;
                CaptureProgress = 0;
                CapturingTeam = 0;
                // scoring is now handled by BeaconMatch (differential)
            }
        }
        else
        {
            // Different team starting to capture — reset progress
            CapturingTeam = team;
            CaptureProgress = 1;
        }
    }

    /// <summary>Force-set beacon state (for replay snapshot application).</summary>
    public void ForceState(int owner, int captureProgress, bool contested)
    {
        OwnerTeam = owner;
        CaptureProgress = captureProgress;
        IsContested = contested;
    }

    /// <summary>Check if a position is inside this beacon zone.</summary>
    public bool Contains(Vector2 pos)
    {
        return pos.DistanceTo(Zone.Center) <= Zone.Radius;
    }
}
