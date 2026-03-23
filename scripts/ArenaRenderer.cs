// ─────────────────────────────────────────────────────────────────────────────
// ArenaRenderer.cs — Draws the arena boundaries
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using AiBtGym.Simulation;

namespace AiBtGym.Godot;

public partial class ArenaRenderer : Node2D
{
    public Arena? Arena { get; set; }

    private static readonly Color WallColor = new(0.3f, 0.35f, 0.5f);
    private static readonly Color SurfaceColor = new(0.4f, 0.5f, 0.7f);

    public override void _Ready()
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Arena == null) return;

        var bounds = Arena.Bounds;

        // Fill background slightly lighter than clear color
        DrawRect(bounds, new Color(0.06f, 0.06f, 0.12f));

        // Draw boundary walls as thick lines
        float t = Arena.WallThickness;
        var tl = bounds.Position;
        var tr = new Vector2(bounds.End.X, bounds.Position.Y);
        var bl = new Vector2(bounds.Position.X, bounds.End.Y);
        var br = bounds.End;

        // Walls
        DrawLine(tl, tr, SurfaceColor, t); // ceiling
        DrawLine(bl, br, WallColor, t);    // floor
        DrawLine(tl, bl, SurfaceColor, t); // left wall
        DrawLine(tr, br, SurfaceColor, t); // right wall

        // Surface attach indicators (dashes along walls/ceiling)
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
}
