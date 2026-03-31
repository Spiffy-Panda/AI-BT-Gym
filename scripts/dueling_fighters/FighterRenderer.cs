// ─────────────────────────────────────────────────────────────────────────────
// FighterRenderer.cs — Draws a fighter as colored spheres + chain lines
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using AiBtGym.Simulation;

namespace AiBtGym.Godot;

public partial class FighterRenderer : Node2D
{
    public Fighter? Fighter { get; set; }
    public Color TeamColor { get; set; } = Colors.Red;

    private static readonly Color ChainExtending = new(1f, 0.9f, 0.2f);  // yellow
    private static readonly Color ChainLocked = new(0.2f, 1f, 0.4f);     // green
    private static readonly Color ChainRetracting = new(1f, 0.5f, 0.1f); // orange
    private static readonly Color AnchorColor = new(0.8f, 0.2f, 1f);     // purple

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Fighter == null) return;

        // Body sphere
        DrawCircle(Fighter.Position, Fighter.BodyRadius, TeamColor);
        DrawArc(Fighter.Position, Fighter.BodyRadius, 0, Mathf.Tau, 32,
            TeamColor.Lightened(0.3f), 2f);

        // Draw each fist
        DrawFist(Fighter.LeftFist);
        DrawFist(Fighter.RightFist);

        // Health bar above body
        DrawHealthBar(Fighter);
    }

    private void DrawFist(Fist fist)
    {
        if (fist.ChainState == FistChainState.Retracted) return;

        // Chain line
        Color chainColor = fist.ChainState switch
        {
            FistChainState.Extending => ChainExtending,
            FistChainState.Locked => ChainLocked,
            FistChainState.Retracting => ChainRetracting,
            _ => Colors.White
        };

        DrawLine(Fighter!.Position, fist.Position, chainColor, 2f);

        // Fist sphere
        Color fistColor = TeamColor.Lightened(0.2f);
        DrawCircle(fist.Position, fist.FistRadius, fistColor);

        // Anchor indicator
        if (fist.IsAttachedToWorld)
        {
            DrawCircle(fist.AnchorPoint, 5f, AnchorColor);
            float s = 4f;
            // Diamond shape
            var points = new Vector2[]
            {
                fist.AnchorPoint + new Vector2(0, -s),
                fist.AnchorPoint + new Vector2(s, 0),
                fist.AnchorPoint + new Vector2(0, s),
                fist.AnchorPoint + new Vector2(-s, 0),
                fist.AnchorPoint + new Vector2(0, -s)
            };
            DrawPolyline(points, AnchorColor, 2f);
        }
    }

    private void DrawHealthBar(Fighter f)
    {
        float barWidth = 40f;
        float barHeight = 4f;
        Vector2 barPos = f.Position + new Vector2(-barWidth / 2f, -f.BodyRadius - 12f);

        // Background
        DrawRect(new Rect2(barPos, new Vector2(barWidth, barHeight)), new Color(0.2f, 0.2f, 0.2f));

        // Fill
        float fill = f.Health / 100f;
        Color barColor = fill > 0.5f ? Colors.Green : fill > 0.25f ? Colors.Yellow : Colors.Red;
        DrawRect(new Rect2(barPos, new Vector2(barWidth * fill, barHeight)), barColor);
    }
}
