// ─────────────────────────────────────────────────────────────────────────────
// ProjectileRenderer.cs — Draws live projectiles with ballistic trails and
//                         rifle flash lines in the Beacon Brawl viewer
// ─────────────────────────────────────────────────────────────────────────────
//
// Pistol bullets: filled circle at current position + fading trail dots
// computed via backward ballistic integration (pos - vel*dt + 0.5*g*dt^2).
//
// Rifle shots: instant flash line drawn from ReplayRifleShot segments,
// fading over a short duration.

using System.Collections.Generic;
using Godot;
using AiBtGym.Simulation.BeaconBrawl;

namespace AiBtGym.Godot.BeaconBrawl;

public partial class ProjectileRenderer : Node2D
{
    public BeaconMatch? Match { get; set; }
    public Color TeamAColor { get; set; } = Colors.Red;
    public Color TeamBColor { get; set; } = Colors.Blue;

    // Rifle flash lines: (tick fired, segment points, team)
    private readonly List<(int tick, Vector2[] segments, int team)> _rifleFlashes = [];
    private const int RifleFlashDuration = 12; // ticks to display flash

    /// <summary>Call each tick from BeaconMain to record rifle shots for flash rendering.</summary>
    public void NotifyRifleShot(Vector2[] segments, int team)
    {
        if (Match == null) return;
        _rifleFlashes.Add((Match.Tick, segments, team));
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Match == null) return;

        DrawPistolBullets();
        DrawRifleFlashes();
    }

    private void DrawPistolBullets()
    {
        foreach (var proj in Match!.Projectiles)
        {
            if (!proj.IsAlive) continue;

            Color teamColor = proj.OwnerTeam == 0 ? TeamAColor : TeamBColor;
            Color bulletColor = teamColor.Lightened(0.4f);

            // Current position: bright filled circle
            DrawCircle(proj.Position, proj.Radius, bulletColor);

            // Ballistic trail: compute past positions using reverse integration
            // pos(t - n*dt) = pos - vel*n*dt + 0.5*g*(n*dt)^2
            float dt = BeaconPhysics.FixedDt;
            float g = proj.Gravity;
            Vector2 vel = proj.Velocity;
            Vector2 pos = proj.Position;

            const int trailDots = 6;
            const int tickSpacing = 2;

            for (int i = 1; i <= trailDots; i++)
            {
                float t = i * tickSpacing * dt;
                // Backward ballistic: where was the bullet t seconds ago?
                float pastX = pos.X - vel.X * t;
                // For Y: need to account for gravity changing velocity over time
                // vel_y at time t ago = vel.Y - g*t  (gravity was adding to vel.Y each tick)
                // pos_y at time t ago = pos.Y - vel.Y*t + 0.5*g*t^2
                float pastY = pos.Y - vel.Y * t + 0.5f * g * t * t;

                float alpha = 1f - (float)i / (trailDots + 1);
                Color trailColor = new(bulletColor.R, bulletColor.G, bulletColor.B, alpha * 0.6f);
                float radius = proj.Radius * (1f - 0.1f * i);
                if (radius < 1f) radius = 1f;

                DrawCircle(new Vector2(pastX, pastY), radius, trailColor);
            }
        }
    }

    private void DrawRifleFlashes()
    {
        int currentTick = Match!.Tick;

        // Remove expired flashes
        _rifleFlashes.RemoveAll(f => currentTick - f.tick > RifleFlashDuration);

        foreach (var (tick, segments, team) in _rifleFlashes)
        {
            float age = currentTick - tick;
            float alpha = 1f - age / RifleFlashDuration;
            if (alpha <= 0) continue;

            Color teamColor = team == 0 ? TeamAColor : TeamBColor;
            Color flashColor = new(teamColor.R, teamColor.G, teamColor.B, alpha * 0.8f);
            float width = 2f * alpha;

            for (int i = 0; i < segments.Length - 1; i++)
            {
                DrawLine(segments[i], segments[i + 1], flashColor, width);
            }
        }
    }
}
