// ─────────────────────────────────────────────────────────────────────────────
// PawnRenderer.cs — Draws a pawn: body, hook, health bar, status indicators
// ─────────────────────────────────────────────────────────────────────────────

using Godot;
using AiBtGym.Simulation;
using AiBtGym.Simulation.BeaconBrawl;

namespace AiBtGym.Godot.BeaconBrawl;

public partial class PawnRenderer : Node2D
{
    public Pawn? Pawn { get; set; }
    public Color TeamColor { get; set; } = Colors.Red;

    private static readonly Color ChainExtending = new(1f, 0.9f, 0.2f);
    private static readonly Color ChainLocked = new(0.2f, 1f, 0.4f);
    private static readonly Color ChainRetracting = new(1f, 0.5f, 0.1f);
    private static readonly Color AnchorColor = new(0.8f, 0.2f, 1f);

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Pawn == null) return;

        // Dead pawns: ghost outline
        if (Pawn.IsDead)
        {
            Color ghostColor = new(TeamColor.R, TeamColor.G, TeamColor.B, 0.2f);
            DrawArc(Pawn.Position, Pawn.BodyRadius, 0, Mathf.Tau, 32, ghostColor, 1f);
            return;
        }

        // Body
        DrawCircle(Pawn.Position, Pawn.BodyRadius, TeamColor);
        DrawArc(Pawn.Position, Pawn.BodyRadius, 0, Mathf.Tau, 32,
            TeamColor.Lightened(0.3f), 2f);

        // Idle weapon indicators (always visible to distinguish roles)
        if (Pawn.Role == PawnRole.Grappler)
            DrawGrapplerIcon();
        else
            DrawGunnerIcon();

        // Hook chain (Grappler only — extends/retracts/anchors)
        if (Pawn.Role == PawnRole.Grappler)
            DrawHook(Pawn.Hook);

        // Health bar above pawn
        float barW = 24f, barH = 3f;
        float barY = Pawn.Position.Y - Pawn.BodyRadius - 8f;
        float barX = Pawn.Position.X - barW / 2f;
        float healthPct = Pawn.HealthPct;
        Color healthColor = healthPct > 0.5f ? Colors.Green : healthPct > 0.25f ? Colors.Yellow : Colors.Red;
        DrawRect(new Rect2(barX, barY, barW, barH), new Color(0.2f, 0.2f, 0.2f));
        DrawRect(new Rect2(barX, barY, barW * healthPct, barH), healthColor);

        // Stun indicator: pulsing white ring
        if (Pawn.IsStunned)
        {
            float stunPulse = 0.5f + 0.5f * Mathf.Sin(Pawn.StunTicksRemaining * 0.5f);
            Color stunColor = new(1f, 1f, 1f, stunPulse);
            DrawArc(Pawn.Position, Pawn.BodyRadius + 4f, 0, Mathf.Tau, 32, stunColor, 2f);
        }

        // Vulnerable indicator: red pulsing ring
        if (Pawn.IsVulnerable)
        {
            float vulnPulse = 0.5f + 0.5f * Mathf.Sin(Pawn.VulnerableTicks * 0.3f);
            Color vulnColor = new(1f, 0.2f, 0.2f, vulnPulse);
            DrawArc(Pawn.Position, Pawn.BodyRadius + 3f, 0, Mathf.Tau, 32, vulnColor, 2f);
        }

        // Parry active: cyan flash
        if (Pawn.IsParryActive)
        {
            Color parryColor = new(0.2f, 1f, 1f, 0.7f);
            DrawArc(Pawn.Position, Pawn.BodyRadius + 6f, 0, Mathf.Tau, 32, parryColor, 3f);
        }

        // Parry cooldown arc
        if (Pawn.ParryCooldown > 0 && !Pawn.IsParryActive)
        {
            float progress = 1f - (float)Pawn.ParryCooldown / Pawn.ParryCooldownMax;
            float arcAngle = progress * Mathf.Tau;
            Color cooldownColor = new(0.3f, 0.8f, 1f, 0.5f);
            DrawArc(Pawn.Position, Pawn.BodyRadius + 2f, -Mathf.Pi / 2f,
                -Mathf.Pi / 2f + arcAngle, 32, cooldownColor, 1.5f);
        }
    }

    /// <summary>Grappler icon: small hook/claw shape inside body.</summary>
    private void DrawGrapplerIcon()
    {
        var p = Pawn!.Position;
        float r = Pawn.BodyRadius * 0.45f;
        Color c = TeamColor.Darkened(0.4f);

        // Hook shape: curved arm + barb
        var points = new Vector2[]
        {
            p + new Vector2(-r * 0.3f, r * 0.5f),
            p + new Vector2(-r * 0.3f, -r * 0.3f),
            p + new Vector2(0, -r * 0.6f),
            p + new Vector2(r * 0.3f, -r * 0.3f),
            p + new Vector2(r * 0.3f, r * 0.5f),
        };
        DrawPolyline(points, c, 2f);
        // Barb tips
        DrawLine(p + new Vector2(-r * 0.3f, r * 0.5f), p + new Vector2(-r * 0.6f, r * 0.3f), c, 2f);
        DrawLine(p + new Vector2(r * 0.3f, r * 0.5f), p + new Vector2(r * 0.6f, r * 0.3f), c, 2f);
    }

    /// <summary>Gunner icon: crosshair/target reticle inside body.</summary>
    private void DrawGunnerIcon()
    {
        var p = Pawn!.Position;
        float r = Pawn.BodyRadius * 0.4f;
        Color c = TeamColor.Darkened(0.4f);

        // Crosshair lines
        DrawLine(p + new Vector2(-r, 0), p + new Vector2(r, 0), c, 1.5f);
        DrawLine(p + new Vector2(0, -r), p + new Vector2(0, r), c, 1.5f);
        // Inner ring
        DrawArc(p, r * 0.6f, 0, Mathf.Tau, 16, c, 1.5f);
    }

    private void DrawHook(Fist hook)
    {
        if (hook.ChainState == FistChainState.Retracted) return;

        Color chainColor = hook.ChainState switch
        {
            FistChainState.Extending => ChainExtending,
            FistChainState.Locked => ChainLocked,
            FistChainState.Retracting => ChainRetracting,
            _ => Colors.White
        };

        DrawLine(Pawn!.Position, hook.Position, chainColor, 2f);

        // Hook tip
        Color tipColor = TeamColor.Lightened(0.2f);
        DrawCircle(hook.Position, hook.FistRadius, tipColor);

        // Anchor indicator
        if (hook.IsAttachedToWorld)
        {
            DrawCircle(hook.AnchorPoint, 5f, AnchorColor);
            float s = 4f;
            var points = new Vector2[]
            {
                hook.AnchorPoint + new Vector2(0, -s),
                hook.AnchorPoint + new Vector2(s, 0),
                hook.AnchorPoint + new Vector2(0, s),
                hook.AnchorPoint + new Vector2(-s, 0),
                hook.AnchorPoint + new Vector2(0, -s)
            };
            DrawPolyline(points, AnchorColor, 2f);
        }
    }
}
