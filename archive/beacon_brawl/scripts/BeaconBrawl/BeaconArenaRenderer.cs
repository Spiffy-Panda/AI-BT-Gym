// ─────────────────────────────────────────────────────────────────────────────
// BeaconArenaRenderer.cs — Draws arena walls, platform, and beacon zones
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using AiBtGym.Simulation.BeaconBrawl;

namespace AiBtGym.Godot.BeaconBrawl;

public partial class BeaconArenaRenderer : Node2D
{
    public BeaconArena? Arena { get; set; }
    public Beacon[]? Beacons { get; set; }

    private static readonly Color WallColor = new(0.3f, 0.35f, 0.5f);
    private static readonly Color SurfaceColor = new(0.4f, 0.5f, 0.7f);
    private static readonly Color PlatformColor = new(0.35f, 0.4f, 0.55f);
    private static readonly Color NeutralBeaconColor = new(0.4f, 0.4f, 0.4f, 0.5f);

    // Team colors for beacon fills (set externally)
    public Color TeamAColor { get; set; } = new(0.9f, 0.2f, 0.2f);
    public Color TeamBColor { get; set; } = new(0.2f, 0.4f, 0.9f);

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Arena == null) return;

        var bounds = Arena.Bounds;

        // Background
        DrawRect(bounds, new Color(0.06f, 0.06f, 0.12f));

        // Walls
        float t = Arena.WallThickness;
        var tl = bounds.Position;
        var tr = new Vector2(bounds.End.X, bounds.Position.Y);
        var bl = new Vector2(bounds.Position.X, bounds.End.Y);
        var br = bounds.End;

        DrawLine(tl, tr, SurfaceColor, t); // ceiling
        DrawLine(bl, br, WallColor, t);    // floor
        DrawLine(tl, bl, SurfaceColor, t); // left wall
        DrawLine(tr, br, SurfaceColor, t); // right wall

        // Center platform
        DrawRect(Arena.Platform, PlatformColor);
        DrawRect(Arena.Platform, SurfaceColor.Darkened(0.1f), false, 2f);

        // Dashes along ceiling
        float dashLen = 12f, gap = 8f;
        Color dashColor = SurfaceColor.Darkened(0.3f);
        for (float x = tl.X; x < tr.X; x += dashLen + gap)
        {
            float endX = Mathf.Min(x + dashLen, tr.X);
            DrawLine(new Vector2(x, tl.Y - 3), new Vector2(endX, tl.Y - 3), dashColor, 2f);
        }

        // Beacon zones
        if (Beacons != null)
        {
            for (int i = 0; i < Beacons.Length; i++)
                DrawBeaconZone(Beacons[i]);
        }
    }

    private void DrawBeaconZone(Beacon beacon)
    {
        var center = beacon.Zone.Center;
        float radius = beacon.Zone.Radius;

        // Ownership fill
        Color fillColor;
        if (beacon.OwnerTeam == 1)
            fillColor = TeamAColor with { A = 0.15f };
        else if (beacon.OwnerTeam == 2)
            fillColor = TeamBColor with { A = 0.15f };
        else
            fillColor = NeutralBeaconColor with { A = 0.05f };

        DrawCircle(center, radius, fillColor);

        // Border ring
        Color ringColor;
        if (beacon.OwnerTeam == 1)
            ringColor = TeamAColor with { A = 0.6f };
        else if (beacon.OwnerTeam == 2)
            ringColor = TeamBColor with { A = 0.6f };
        else
            ringColor = NeutralBeaconColor;

        // Dashed ring
        int segments = 24;
        for (int j = 0; j < segments; j += 2)
        {
            float a1 = (j / (float)segments) * Mathf.Tau;
            float a2 = ((j + 1) / (float)segments) * Mathf.Tau;
            var p1 = center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius;
            var p2 = center + new Vector2(Mathf.Cos(a2), Mathf.Sin(a2)) * radius;
            DrawLine(p1, p2, ringColor, 2f);
        }

        // Capture progress arc
        if (beacon.CaptureProgress > 0 && beacon.CapturingTeam > 0)
        {
            Color capColor = beacon.CapturingTeam == 1
                ? TeamAColor with { A = 0.8f }
                : TeamBColor with { A = 0.8f };
            float progress = (float)beacon.CaptureProgress / Beacon.CaptureThreshold;
            float arcAngle = progress * Mathf.Tau;
            DrawArc(center, radius + 4f, -Mathf.Pi / 2f, -Mathf.Pi / 2f + arcAngle, 32, capColor, 3f);
        }

        // Contested indicator: pulsing X
        if (beacon.IsContested)
        {
            float s = 8f;
            Color contested = new(1f, 1f, 0.3f, 0.7f);
            DrawLine(center + new Vector2(-s, -s), center + new Vector2(s, s), contested, 2f);
            DrawLine(center + new Vector2(s, -s), center + new Vector2(-s, s), contested, 2f);
        }

        // Point multiplier label
        if (beacon.Zone.PointMultiplier > 1)
        {
            // Small "2x" indicator above beacon
            var labelPos = center + new Vector2(-8, -radius - 12);
            DrawString(ThemeDB.FallbackFont, labelPos, "2x",
                HorizontalAlignment.Left, -1, 12, new Color(1f, 0.9f, 0.3f, 0.7f));
        }
    }
}
