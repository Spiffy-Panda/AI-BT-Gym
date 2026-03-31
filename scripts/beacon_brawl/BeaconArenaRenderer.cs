// ─────────────────────────────────────────────────────────────────────────────
// BeaconArenaRenderer.cs — Draws arena walls, platform, and beacon zones
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using AiBtGym.Simulation;
using AiBtGym.Simulation.BeaconBrawl;

namespace AiBtGym.Godot.BeaconBrawl;

public partial class BeaconArenaRenderer : Node2D
{
    public BeaconArena? Arena { get; set; }
    public Beacon[]? Beacons { get; set; }

    /// <summary>Optional match reference for rendering mutable modifier state (walls, pickups, shrink).</summary>
    public BeaconMatch? Match { get; set; }

    private static readonly Color WallColor = new(0.3f, 0.35f, 0.5f);
    private static readonly Color SurfaceColor = new(0.4f, 0.5f, 0.7f);
    private static readonly Color PlatformColor = new(0.35f, 0.4f, 0.55f);
    private static readonly Color NeutralBeaconColor = new(0.4f, 0.4f, 0.4f, 0.5f);

    // Modifier feature colors (mirrored from ArenaRenderer)
    private static readonly Color HazardColor = new(0.5f, 0.12f, 0.12f, 0.4f);
    private static readonly Color HazardBorderColor = new(0.7f, 0.15f, 0.15f);
    private static readonly Color FrictionZoneColor = new(0.3f, 0.5f, 0.3f, 0.25f);
    private static readonly Color BumperColor = new(0.6f, 0.6f, 0.2f);
    private static readonly Color PickupColor = new(0.2f, 0.8f, 0.3f);
    private static readonly Color PickupInactiveColor = new(0.2f, 0.8f, 0.3f, 0.2f);
    private static readonly Color ModPlatformColor = new(0.35f, 0.45f, 0.65f);
    private static readonly Color DestructibleWallColor = new(0.5f, 0.4f, 0.3f);
    private static readonly Color DestructibleWallDamagedColor = new(0.6f, 0.3f, 0.2f);
    private static readonly Color ShrinkBorderColor = new(0.8f, 0.2f, 0.2f, 0.6f);

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
        var mods = Arena.Modifiers;

        // Background
        DrawRect(bounds, new Color(0.06f, 0.06f, 0.12f));

        // Modifier features (drawn before walls so they appear under)
        DrawHazardZones(mods, bounds);
        DrawWallFrictionZones(mods, bounds);

        // Walls
        float t = Arena.WallThickness;
        var tl = bounds.Position;
        var tr = new Vector2(bounds.End.X, bounds.Position.Y);
        var bl = new Vector2(bounds.Position.X, bounds.End.Y);
        var br = bounds.End;

        // Ceiling (potentially shaped)
        if (mods.Ceiling != null)
            DrawShapedCeiling(mods, bounds, t);
        else
            DrawLine(tl, tr, SurfaceColor, t); // flat ceiling

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

        // Modifier features (drawn after walls)
        DrawCornerBumpers(mods, bounds);
        DrawModifierPlatforms(mods);
        DrawDestructibleWalls(mods);
        DrawPickups(mods);
        DrawShrinkBorder(mods, bounds);

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

    // ── Modifier feature drawing (ported from ArenaRenderer) ──

    private void DrawHazardZones(ArenaConfig mods, Rect2 bounds)
    {
        foreach (var hz in mods.HazardZones)
        {
            float floorY = bounds.End.Y;
            var rect = new Rect2(hz.X, floorY - 5f, hz.Width, 5f);
            DrawRect(rect, HazardColor);
            DrawLine(new Vector2(hz.X, floorY - 5f), new Vector2(hz.X, floorY), HazardBorderColor, 1f);
            DrawLine(new Vector2(hz.X + hz.Width, floorY - 5f), new Vector2(hz.X + hz.Width, floorY), HazardBorderColor, 1f);
        }
    }

    private void DrawWallFrictionZones(ArenaConfig mods, Rect2 bounds)
    {
        foreach (var wfz in mods.WallFrictionZones)
        {
            float x = wfz.Side == "left" ? bounds.Position.X : bounds.End.X - 6f;
            var rect = new Rect2(x, wfz.TopY, 6f, wfz.BottomY - wfz.TopY);
            DrawRect(rect, FrictionZoneColor);
        }
    }

    private void DrawShapedCeiling(ArenaConfig mods, Rect2 bounds, float thickness)
    {
        if (mods.Ceiling is not CeilingConfig) return;
        int segments = 20;
        float startX = bounds.Position.X;
        float endX = bounds.End.X;
        float step = (endX - startX) / segments;
        for (int i = 0; i < segments; i++)
        {
            float x0 = startX + i * step;
            float x1 = startX + (i + 1) * step;
            float y0 = Arena!.GetCeilingY(x0);
            float y1 = Arena.GetCeilingY(x1);
            DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), SurfaceColor, thickness);
        }
    }

    private void DrawCornerBumpers(ArenaConfig mods, Rect2 bounds)
    {
        foreach (var bumper in mods.CornerBumpers)
        {
            Vector2 p1, p2;
            float size = bumper.Size;
            switch (bumper.Corner)
            {
                case "top_left":
                    p1 = bounds.Position + new Vector2(size, 0);
                    p2 = bounds.Position + new Vector2(0, size);
                    break;
                case "top_right":
                    p1 = new Vector2(bounds.End.X - size, bounds.Position.Y);
                    p2 = new Vector2(bounds.End.X, bounds.Position.Y + size);
                    break;
                case "bottom_left":
                    p1 = new Vector2(bounds.Position.X, bounds.End.Y - size);
                    p2 = new Vector2(bounds.Position.X + size, bounds.End.Y);
                    break;
                case "bottom_right":
                    p1 = new Vector2(bounds.End.X, bounds.End.Y - size);
                    p2 = new Vector2(bounds.End.X - size, bounds.End.Y);
                    break;
                default: continue;
            }
            DrawLine(p1, p2, BumperColor, 3f);
        }
    }

    private void DrawModifierPlatforms(ArenaConfig mods)
    {
        foreach (var plat in mods.Platforms)
        {
            float left = plat.X - plat.Width / 2f;
            var rect = new Rect2(left, plat.Y, plat.Width, plat.Height);
            DrawRect(rect, ModPlatformColor);

            Color dashColor = ModPlatformColor.Lightened(0.2f);
            float dashLen = 8f, gap = 6f;
            for (float x = left; x < left + plat.Width; x += dashLen + gap)
            {
                float endX = Mathf.Min(x + dashLen, left + plat.Width);
                DrawLine(new Vector2(x, plat.Y - 2), new Vector2(endX, plat.Y - 2), dashColor, 2f);
            }

            if (plat.OneWay)
            {
                Color arrowColor = ModPlatformColor.Darkened(0.2f);
                float mid = plat.X;
                DrawLine(new Vector2(mid, plat.Y + plat.Height + 4), new Vector2(mid, plat.Y + plat.Height + 1), arrowColor, 1f);
            }
        }
    }

    private void DrawDestructibleWalls(ArenaConfig mods)
    {
        for (int i = 0; i < mods.DestructibleWalls.Count; i++)
        {
            bool exists = Match?.DestructibleWallExists[i] ?? true;
            if (!exists) continue;

            var wall = mods.DestructibleWalls[i];
            float hp = Match?.DestructibleWallHp[i] ?? wall.Hp;
            float hpFraction = hp / wall.Hp;

            float left = wall.X - wall.Thickness / 2f;
            float top = wall.BottomY - wall.Height;
            var rect = new Rect2(left, top, wall.Thickness, wall.Height);

            Color color = DestructibleWallColor.Lerp(DestructibleWallDamagedColor, 1f - hpFraction);
            DrawRect(rect, color);

            if (hpFraction < 0.7f)
            {
                Color crackColor = new(0.2f, 0.15f, 0.1f, 0.5f);
                float midY = top + wall.Height * 0.4f;
                DrawLine(new Vector2(left, midY), new Vector2(left + wall.Thickness, midY + 10), crackColor, 1f);
            }
            if (hpFraction < 0.4f)
            {
                Color crackColor = new(0.2f, 0.15f, 0.1f, 0.7f);
                float midY2 = top + wall.Height * 0.7f;
                DrawLine(new Vector2(left + wall.Thickness, midY2), new Vector2(left, midY2 - 8), crackColor, 1f);
            }
        }
    }

    private void DrawPickups(ArenaConfig mods)
    {
        for (int i = 0; i < mods.Pickups.Count; i++)
        {
            var pickup = mods.Pickups[i];
            bool active = Match?.PickupActive[i] ?? true;
            Color color = active ? PickupColor : PickupInactiveColor;

            float size = 6f;
            var pos = new Vector2(pickup.X, pickup.Y);
            DrawLine(pos - new Vector2(size, 0), pos + new Vector2(size, 0), color, 2f);
            DrawLine(pos - new Vector2(0, size), pos + new Vector2(0, size), color, 2f);

            if (active)
                DrawCircle(pos, 10f, new Color(color.R, color.G, color.B, 0.15f));
        }
    }

    private void DrawShrinkBorder(ArenaConfig mods, Rect2 bounds)
    {
        if (mods.Shrink == null || Match == null) return;

        float effLeft = Match.EffectiveLeft;
        float effRight = Match.EffectiveRight;

        if (effLeft <= bounds.Position.X + 1f && effRight >= bounds.End.X - 1f) return;

        DrawLine(new Vector2(effLeft, bounds.Position.Y), new Vector2(effLeft, bounds.End.Y), ShrinkBorderColor, 3f);
        DrawLine(new Vector2(effRight, bounds.Position.Y), new Vector2(effRight, bounds.End.Y), ShrinkBorderColor, 3f);

        Color deadZoneColor = new(0.5f, 0.1f, 0.1f, 0.15f);
        if (effLeft > bounds.Position.X)
            DrawRect(new Rect2(bounds.Position.X, bounds.Position.Y, effLeft - bounds.Position.X, bounds.Size.Y), deadZoneColor);
        if (effRight < bounds.End.X)
            DrawRect(new Rect2(effRight, bounds.Position.Y, bounds.End.X - effRight, bounds.Size.Y), deadZoneColor);
    }
}
