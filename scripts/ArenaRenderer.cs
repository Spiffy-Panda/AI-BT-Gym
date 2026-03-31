// ─────────────────────────────────────────────────────────────────────────────
// ArenaRenderer.cs — Draws the arena boundaries and map features
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using AiBtGym.Simulation;

namespace AiBtGym.Godot;

public partial class ArenaRenderer : Node2D
{
    public Arena? Arena { get; set; }

    /// <summary>Optional match reference for rendering mutable feature state (walls, pickups, shrink).</summary>
    public Match? Match { get; set; }

    private static readonly Color WallColor = new(0.3f, 0.35f, 0.5f);
    private static readonly Color SurfaceColor = new(0.4f, 0.5f, 0.7f);
    private static readonly Color PlatformColor = new(0.35f, 0.45f, 0.65f);
    private static readonly Color HazardColor = new(0.5f, 0.12f, 0.12f, 0.4f);
    private static readonly Color HazardBorderColor = new(0.7f, 0.15f, 0.15f);
    private static readonly Color FrictionZoneColor = new(0.3f, 0.5f, 0.3f, 0.25f);
    private static readonly Color BumperColor = new(0.6f, 0.6f, 0.2f);
    private static readonly Color PickupColor = new(0.2f, 0.8f, 0.3f);
    private static readonly Color PickupInactiveColor = new(0.2f, 0.8f, 0.3f, 0.2f);
    private static readonly Color DestructibleWallColor = new(0.5f, 0.4f, 0.3f);
    private static readonly Color DestructibleWallDamagedColor = new(0.6f, 0.3f, 0.2f);
    private static readonly Color ShrinkBorderColor = new(0.8f, 0.2f, 0.2f, 0.6f);

    public override void _Ready()
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Arena == null) return;

        var bounds = Arena.Bounds;
        var config = Arena.Config;

        // Fill background slightly lighter than clear color
        DrawRect(bounds, new Color(0.06f, 0.06f, 0.12f));

        // Draw hazard zones (before walls, so they appear under)
        DrawHazardZones(config, bounds);

        // Draw wall friction zones
        DrawWallFrictionZones(config, bounds);

        // Draw boundary walls as thick lines
        float t = Arena.WallThickness;
        var tl = bounds.Position;
        var tr = new Vector2(bounds.End.X, bounds.Position.Y);
        var bl = new Vector2(bounds.Position.X, bounds.End.Y);
        var br = bounds.End;

        // Ceiling (potentially non-flat)
        if (config.Ceiling != null)
            DrawShapedCeiling(config, bounds, t);
        else
            DrawLine(tl, tr, SurfaceColor, t); // flat ceiling

        // Floor, left wall, right wall
        DrawLine(bl, br, WallColor, t);    // floor
        DrawLine(tl, bl, SurfaceColor, t); // left wall
        DrawLine(tr, br, SurfaceColor, t); // right wall

        // Surface attach indicators (dashes along walls/ceiling)
        DrawSurfaceDashes(tl, tr, bl, br);

        // Corner bumpers
        DrawCornerBumpers(config, bounds);

        // Platforms
        DrawPlatforms(config);

        // Destructible walls
        DrawDestructibleWalls(config);

        // Pickups
        DrawPickups(config);

        // Arena shrink border
        DrawShrinkBorder(config, bounds);
    }

    private void DrawSurfaceDashes(Vector2 tl, Vector2 tr, Vector2 bl, Vector2 br)
    {
        float dashLen = 12f;
        float gap = 8f;
        Color dashColor = SurfaceColor.Darkened(0.3f);

        // Ceiling dashes
        for (float x = tl.X; x < tr.X; x += dashLen + gap)
        {
            float endX = Mathf.Min(x + dashLen, tr.X);
            DrawLine(new Vector2(x, tl.Y - 3), new Vector2(endX, tl.Y - 3), dashColor, 2f);
        }

        // Left wall dashes
        for (float y = tl.Y; y < bl.Y; y += dashLen + gap)
        {
            float endY = Mathf.Min(y + dashLen, bl.Y);
            DrawLine(new Vector2(tl.X - 3, y), new Vector2(tl.X - 3, endY), dashColor, 2f);
        }

        // Right wall dashes
        for (float y = tr.Y; y < br.Y; y += dashLen + gap)
        {
            float endY = Mathf.Min(y + dashLen, br.Y);
            DrawLine(new Vector2(tr.X + 3, y), new Vector2(tr.X + 3, endY), dashColor, 2f);
        }
    }

    private void DrawShapedCeiling(ArenaConfig config, Rect2 bounds, float thickness)
    {
        if (config.Ceiling is not CeilingConfig ceil) return;

        // Draw ceiling as segmented line following the shape
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

    private void DrawHazardZones(ArenaConfig config, Rect2 bounds)
    {
        foreach (var hz in config.HazardZones)
        {
            float floorY = bounds.End.Y;
            var rect = new Rect2(hz.X, floorY - 5f, hz.Width, 5f);
            DrawRect(rect, HazardColor);
            // Border lines
            DrawLine(new Vector2(hz.X, floorY - 5f), new Vector2(hz.X, floorY), HazardBorderColor, 1f);
            DrawLine(new Vector2(hz.X + hz.Width, floorY - 5f), new Vector2(hz.X + hz.Width, floorY), HazardBorderColor, 1f);
        }
    }

    private void DrawWallFrictionZones(ArenaConfig config, Rect2 bounds)
    {
        foreach (var wfz in config.WallFrictionZones)
        {
            float x = wfz.Side == "left" ? bounds.Position.X : bounds.End.X - 6f;
            var rect = new Rect2(x, wfz.TopY, 6f, wfz.BottomY - wfz.TopY);
            DrawRect(rect, FrictionZoneColor);
        }
    }

    private void DrawCornerBumpers(ArenaConfig config, Rect2 bounds)
    {
        foreach (var bumper in config.CornerBumpers)
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
                default:
                    continue;
            }

            DrawLine(p1, p2, BumperColor, 3f);
        }
    }

    private void DrawPlatforms(ArenaConfig config)
    {
        foreach (var plat in config.Platforms)
        {
            float left = plat.X - plat.Width / 2f;
            var rect = new Rect2(left, plat.Y, plat.Width, plat.Height);
            DrawRect(rect, PlatformColor);

            // Surface dashes on top
            Color dashColor = PlatformColor.Lightened(0.2f);
            float dashLen = 8f, gap = 6f;
            for (float x = left; x < left + plat.Width; x += dashLen + gap)
            {
                float endX = Mathf.Min(x + dashLen, left + plat.Width);
                DrawLine(new Vector2(x, plat.Y - 2), new Vector2(endX, plat.Y - 2), dashColor, 2f);
            }

            // One-way indicator: draw small upward arrows
            if (plat.OneWay)
            {
                Color arrowColor = PlatformColor.Darkened(0.2f);
                float mid = plat.X;
                DrawLine(new Vector2(mid, plat.Y + plat.Height + 4), new Vector2(mid, plat.Y + plat.Height + 1), arrowColor, 1f);
            }
        }
    }

    private void DrawDestructibleWalls(ArenaConfig config)
    {
        for (int i = 0; i < config.DestructibleWalls.Count; i++)
        {
            bool exists = Match?.DestructibleWallExists[i] ?? true;
            if (!exists) continue;

            var wall = config.DestructibleWalls[i];
            float hp = Match?.DestructibleWallHp[i] ?? wall.Hp;
            float hpFraction = hp / wall.Hp;

            float left = wall.X - wall.Thickness / 2f;
            float top = wall.BottomY - wall.Height;
            var rect = new Rect2(left, top, wall.Thickness, wall.Height);

            // Color shifts from normal to damaged as HP drops
            Color color = DestructibleWallColor.Lerp(DestructibleWallDamagedColor, 1f - hpFraction);
            DrawRect(rect, color);

            // Crack lines when damaged
            if (hpFraction < 0.7f)
            {
                Color crackColor = new(0.2f, 0.15f, 0.1f, 0.5f);
                float midX = wall.X;
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

    private void DrawPickups(ArenaConfig config)
    {
        for (int i = 0; i < config.Pickups.Count; i++)
        {
            var pickup = config.Pickups[i];
            bool active = Match?.PickupActive[i] ?? true;
            Color color = active ? PickupColor : PickupInactiveColor;

            // Draw pickup as a small cross/plus
            float size = 6f;
            var pos = new Vector2(pickup.X, pickup.Y);
            DrawLine(pos - new Vector2(size, 0), pos + new Vector2(size, 0), color, 2f);
            DrawLine(pos - new Vector2(0, size), pos + new Vector2(0, size), color, 2f);

            if (active)
                DrawCircle(pos, 10f, new Color(color.R, color.G, color.B, 0.15f));
        }
    }

    private void DrawShrinkBorder(ArenaConfig config, Rect2 bounds)
    {
        if (config.Shrink == null || Match == null) return;

        float effLeft = Match.EffectiveLeft;
        float effRight = Match.EffectiveRight;

        // Only draw if bounds have actually shrunk
        if (effLeft <= bounds.Position.X + 1f && effRight >= bounds.End.X - 1f) return;

        // Draw pulsing vertical lines at effective boundaries
        DrawLine(new Vector2(effLeft, bounds.Position.Y), new Vector2(effLeft, bounds.End.Y), ShrinkBorderColor, 3f);
        DrawLine(new Vector2(effRight, bounds.Position.Y), new Vector2(effRight, bounds.End.Y), ShrinkBorderColor, 3f);

        // Shade the dead zones
        Color deadZoneColor = new(0.5f, 0.1f, 0.1f, 0.15f);
        if (effLeft > bounds.Position.X)
            DrawRect(new Rect2(bounds.Position.X, bounds.Position.Y, effLeft - bounds.Position.X, bounds.Size.Y), deadZoneColor);
        if (effRight < bounds.End.X)
            DrawRect(new Rect2(effRight, bounds.Position.Y, bounds.End.X - effRight, bounds.Size.Y), deadZoneColor);
    }
}
