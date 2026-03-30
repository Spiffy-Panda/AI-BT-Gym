// ─────────────────────────────────────────────────────────────────────────────
// BeaconMatch.cs — Orchestrates a Beacon Brawl v2 match (roles, health, respawn)
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using Godot;
using AiBtGym.BehaviorTree;

namespace AiBtGym.Simulation.BeaconBrawl;

public class BeaconMatch
{
    public BeaconArena Arena { get; }
    public Pawn[] TeamA { get; }
    public Pawn[] TeamB { get; }
    public Pawn[] AllPawns { get; }
    public Beacon[] Beacons { get; }
    public int[] Scores { get; } = [0, 0];
    public int[] Kills { get; } = [0, 0];
    public int[] Rates { get; } = [0, 0];

    public List<Projectile> Projectiles { get; } = [];

    public int Tick { get; set; }
    public int MaxTicks { get; set; } = 90 * 60; // 90 seconds at 60fps
    public int TargetScore { get; set; } = 150;
    public const int KillScoreBonus = 0;
    public const int BaseMaxTicks = 90 * 60;
    public const int OvertimeExtension = 15 * 60; // 15 seconds per extension
    public const int MaxOvertimeTicks = 135 * 60;  // cap at 135s total (45s OT)
    public const float OvertimeThreshold = 0.20f;  // 20% score difference threshold
    public bool IsOver { get; set; }
    public bool IsOvertime => Tick > BaseMaxTicks;

    /// <summary>Winning team index (0=A, 1=B, -1=draw).</summary>
    public int WinnerTeam { get; set; } = -1;

    public BeaconRecorder? Recorder { get; set; }

    /// <summary>Fired when a charged rifle shot resolves. Args: (segments, team).</summary>
    public Action<Vector2[], int>? OnRifleFired { get; set; }

    private readonly BehaviorTreeRunner[] _btRunners;

    /// <summary>
    /// Create a match. Each team has PawnTrees and PawnRoles arrays (same length, one per pawn).
    /// PawnRoles[i] specifies whether pawn i is a Grappler or Gunner.
    /// </summary>
    public BeaconMatch(BeaconArena arena,
        List<BtNode>[] teamATrees, PawnRole[] teamARoles,
        List<BtNode>[] teamBTrees, PawnRole[] teamBRoles,
        int? seed = null)
    {
        Arena = arena;
        int teamSize = teamATrees.Length;

        // Spawn positions: use arena spawn positions with slight offset per pawn
        float spacing = 50f;
        var rng = seed is int s ? new Random(s) : null;
        bool swap = rng?.Next(2) == 1;

        Vector2 spawnA = swap ? arena.SpawnPositions[1] : arena.SpawnPositions[0];
        Vector2 spawnB = swap ? arena.SpawnPositions[0] : arena.SpawnPositions[1];

        TeamA = new Pawn[teamSize];
        TeamB = new Pawn[teamSize];

        for (int i = 0; i < teamSize; i++)
        {
            float offset = (i - (teamSize - 1) / 2f) * spacing;
            float jitter = rng != null ? (float)(rng.NextDouble() * 20 - 10) : 0f;

            TeamA[i] = new Pawn(0, i, teamARoles[i], spawnA + new Vector2(offset + jitter, 0));
            TeamB[i] = new Pawn(1, i, teamBRoles[i], spawnB + new Vector2(offset + jitter, 0));
        }

        AllPawns = new Pawn[teamSize * 2];
        Array.Copy(TeamA, 0, AllPawns, 0, teamSize);
        Array.Copy(TeamB, 0, AllPawns, teamSize, teamSize);

        // Create beacons from arena zones
        Beacons = new Beacon[arena.BeaconZones.Length];
        for (int i = 0; i < arena.BeaconZones.Length; i++)
            Beacons[i] = new Beacon(arena.BeaconZones[i]);

        // Create BT runners (one per pawn)
        _btRunners = new BehaviorTreeRunner[teamSize * 2];
        for (int i = 0; i < teamSize; i++)
        {
            _btRunners[i] = new BehaviorTreeRunner(teamATrees[i]);
            _btRunners[teamSize + i] = new BehaviorTreeRunner(teamBTrees[i]);
        }
    }

    /// <summary>Advance the match by one simulation tick.</summary>
    public void Step()
    {
        if (IsOver) return;

        int teamSize = TeamA.Length;
        var teamAList = new List<Pawn>(TeamA);
        var teamBList = new List<Pawn>(TeamB);

        // ── Respawn check ──
        foreach (var pawn in AllPawns)
        {
            if (pawn.IsDead && pawn.RespawnTimer <= 0)
            {
                Vector2 spawnPos = Arena.SpawnPositions[pawn.TeamIndex];
                pawn.Respawn(spawnPos);
                Recorder?.RecordEvent(Tick, pawn.TeamIndex, pawn.PawnIndex, "respawn");
            }
        }

        // ── Run behavior trees ──
        for (int i = 0; i < teamSize; i++)
        {
            if (TeamA[i].IsDead) continue;
            var allies = new List<Pawn>(teamAList);
            allies.Remove(TeamA[i]);
            var ctx = new PawnBtContext(TeamA[i], allies, teamBList, Beacons, Arena, Tick,
                Scores, Rates, Projectiles, AllPawns, this);
            if (Recorder != null)
            {
                int pawnIdx = i;
                ctx.OnActionExecuted = action => Recorder.RecordAction(Tick, 0, pawnIdx, action);
                ctx.OnPistolFired = (pos, vel) => Recorder.RecordPistolShot(Tick, 0, pawnIdx, pos, vel);
                ctx.OnRifleFired = (segments, hit) => Recorder.RecordRifleShot(Tick, 0, pawnIdx, segments, hit);
                ctx.OnHookGrab = target => Recorder.RecordEvent(Tick, 0, pawnIdx, $"hook_grab_{target.TeamIndex}_{target.PawnIndex}");
                ctx.OnFistHit = target => Recorder.RecordEvent(Tick, 0, pawnIdx, $"fist_hit_{target.TeamIndex}_{target.PawnIndex}");
                ctx.OnDamageDealt = (team, amount) => Recorder.RecordDamage(team, amount);
                ctx.OnParrySuccess = team => Recorder.RecordEvent(Tick, team, -1, "parry_success");
                ctx.OnPistolHit = target => Recorder.RecordEvent(Tick, 0, pawnIdx, $"pistol_hit_{target.TeamIndex}_{target.PawnIndex}");
            }
            if (TeamA[i].CanAct)
                _btRunners[i].Apply(ctx);
        }

        for (int i = 0; i < teamSize; i++)
        {
            if (TeamB[i].IsDead) continue;
            var allies = new List<Pawn>(teamBList);
            allies.Remove(TeamB[i]);
            var ctx = new PawnBtContext(TeamB[i], allies, teamAList, Beacons, Arena, Tick,
                Scores, Rates, Projectiles, AllPawns, this);
            if (Recorder != null)
            {
                int pawnIdx = i;
                ctx.OnActionExecuted = action => Recorder.RecordAction(Tick, 1, pawnIdx, action);
                ctx.OnPistolFired = (pos, vel) => Recorder.RecordPistolShot(Tick, 1, pawnIdx, pos, vel);
                ctx.OnRifleFired = (segments, hit) => Recorder.RecordRifleShot(Tick, 1, pawnIdx, segments, hit);
                ctx.OnHookGrab = target => Recorder.RecordEvent(Tick, 1, pawnIdx, $"hook_grab_{target.TeamIndex}_{target.PawnIndex}");
                ctx.OnFistHit = target => Recorder.RecordEvent(Tick, 1, pawnIdx, $"fist_hit_{target.TeamIndex}_{target.PawnIndex}");
                ctx.OnDamageDealt = (team, amount) => Recorder.RecordDamage(team, amount);
                ctx.OnParrySuccess = team => Recorder.RecordEvent(Tick, team, -1, "parry_success");
                ctx.OnPistolHit = target => Recorder.RecordEvent(Tick, 1, pawnIdx, $"pistol_hit_{target.TeamIndex}_{target.PawnIndex}");
            }
            if (TeamB[i].CanAct)
                _btRunners[teamSize + i].Apply(ctx);
        }

        // ── Hook grab checks (point-blank vulnerability) ──
        foreach (var pawn in AllPawns)
        {
            if (pawn.IsDead || pawn.Role != PawnRole.Grappler) continue;
            var enemies = pawn.TeamIndex == 0 ? teamBList : teamAList;
            var grabbed = BeaconPhysics.CheckHookGrab(pawn, enemies);
            if (grabbed != null)
                Recorder?.RecordEvent(Tick, pawn.TeamIndex, pawn.PawnIndex,
                    $"hook_grab_{grabbed.TeamIndex}_{grabbed.PawnIndex}");
        }

        // ── Physics for living pawns ──
        foreach (var pawn in AllPawns)
        {
            if (pawn.IsDead) continue;
            TickPawn(pawn);
        }

        // ── Projectile physics ──
        BeaconPhysics.TickProjectiles(Projectiles, AllPawns, Arena, Recorder, Tick);

        // ── Body-body collisions ──
        for (int i = 0; i < TeamA.Length; i++)
            for (int j = 0; j < TeamB.Length; j++)
                BeaconPhysics.CheckBodyCollision(TeamA[i], TeamB[j]);

        // ── Hook-hook collisions ──
        for (int i = 0; i < TeamA.Length; i++)
            for (int j = 0; j < TeamB.Length; j++)
                if (TeamA[i].Role == PawnRole.Grappler && TeamB[j].Role == PawnRole.Grappler)
                    BeaconPhysics.CheckHookCollision(TeamA[i].Hook, TeamB[j].Hook);

        // ── Check for kills (health reached 0 this tick) ──
        foreach (var pawn in AllPawns)
        {
            if (pawn.IsDead && pawn.RespawnTimer == Pawn.RespawnDuration)
            {
                // Just died this tick
                int killerTeam = 1 - pawn.TeamIndex;
                Kills[killerTeam]++;
                Scores[killerTeam] += KillScoreBonus;
                Recorder?.RecordEvent(Tick, pawn.TeamIndex, pawn.PawnIndex, "death");
                Recorder?.RecordEvent(Tick, killerTeam, -1, "kill");
            }
        }

        // ── Timer ticks ──
        foreach (var pawn in AllPawns)
            pawn.TickTimers();

        // ── Resolve charged rifle shots (fire after delay expires) ──
        foreach (var pawn in AllPawns)
        {
            if (pawn.IsDead || pawn.Role != PawnRole.Gunner) continue;
            // RifleChargeTicks was just decremented by TickTimers; fire when it hits 0
            if (pawn.RifleChargeTicks == 0 && pawn.RifleChargeDir != Vector2.Zero)
            {
                // Cancel shot if weapon was locked out during the charge (e.g. enemy parry landed)
                if (pawn.RifleLockoutTicks > 0)
                {
                    pawn.RifleChargeDir = Vector2.Zero;
                    continue;
                }

                int lockoutBefore = pawn.RifleLockoutTicks;
                var hitPawn = BeaconPhysics.FireRifle(pawn, pawn.RifleChargeDir, AllPawns, Arena, out var segments);
                Recorder?.RecordRifleShot(Tick, pawn.TeamIndex, pawn.PawnIndex, segments, hitPawn);
                if (hitPawn != null)
                    Recorder?.RecordDamage(pawn.TeamIndex, Pawn.RifleDamage);
                if (pawn.RifleLockoutTicks > lockoutBefore)
                    Recorder?.RecordEvent(Tick, hitPawn?.TeamIndex ?? (1 - pawn.TeamIndex), -1, "parry_success");
                // Notify the projectile renderer for live viewing
                OnRifleFired?.Invoke(segments.ToArray(), pawn.TeamIndex);
                pawn.RifleChargeDir = Vector2.Zero;
            }
        }

        // ── Beacon zone checks (only count living pawns) ──
        foreach (var beacon in Beacons)
        {
            int aCount = 0, bCount = 0;
            foreach (var p in TeamA) if (!p.IsDead && beacon.Contains(p.Position)) aCount++;
            foreach (var p in TeamB) if (!p.IsDead && beacon.Contains(p.Position)) bCount++;
            beacon.Tick(aCount, bCount);
        }

        // ── Flat scoring with center beacon multiplier ──
        int rateA = 0, rateB = 0;
        bool aHoldsCenter = false, bHoldsCenter = false;

        foreach (var beacon in Beacons)
        {
            if (beacon.Zone.Index == 1) // center beacon
            {
                if (beacon.OwnerTeam == 1) aHoldsCenter = true;
                else if (beacon.OwnerTeam == 2) bHoldsCenter = true;
            }
            else // side beacons
            {
                if (beacon.OwnerTeam == 1) rateA += beacon.Zone.PointMultiplier;
                else if (beacon.OwnerTeam == 2) rateB += beacon.Zone.PointMultiplier;
            }
        }

        // Center doubles your side beacon income; alone it scores 1pt/tick
        if (aHoldsCenter) rateA = rateA > 0 ? rateA * 2 : 1;
        if (bHoldsCenter) rateB = rateB > 0 ? rateB * 2 : 1;

        Rates[0] = rateA;
        Rates[1] = rateB;

        if (Tick > 0 && Tick % Beacon.ScoreInterval == 0)
        {
            Scores[0] += rateA;
            Scores[1] += rateB;
        }

        // ── Regen ──
        foreach (var pawn in AllPawns)
        {
            if (pawn.IsDead) continue;
            int myTeamId = pawn.TeamIndex + 1;
            int ownedBeacons = 0;
            foreach (var b in Beacons)
                if (b.OwnerTeam == myTeamId) ownedBeacons++;
            bool inBase = Arena.BaseZones[pawn.TeamIndex].Contains(pawn.Position);
            BeaconPhysics.ApplyRegen(pawn, ownedBeacons, inBase, BeaconPhysics.FixedDt);
        }

        // ── Record tick ──
        Recorder?.RecordTick(this);

        Tick++;
        CheckWinConditions();
    }

    private void TickPawn(Pawn p)
    {
        var pos = p.Position;
        var vel = p.Velocity;

        BeaconPhysics.Integrate(ref pos, ref vel, BeaconPhysics.FixedDt, p.IsGrounded);

        p.Position = pos;
        p.Velocity = vel;

        // Hook chain constraint (Grappler only)
        if (p.Role == PawnRole.Grappler)
            BeaconPhysics.ApplyChainConstraint(p, p.Hook, BeaconPhysics.FixedDt);

        // Platform collision
        pos = p.Position;
        vel = p.Velocity;
        Arena.ResolvePlatformCollision(ref pos, ref vel, p.BodyRadius);
        p.Position = pos;
        p.Velocity = vel;

        // Arena bounds
        pos = p.Position;
        vel = p.Velocity;
        Arena.ClampToArena(ref pos, ref vel, p.BodyRadius);
        p.Position = pos;
        p.Velocity = vel;

        // Ground check
        p.IsGrounded = Arena.IsOnGround(p.Position, p.BodyRadius) ||
                        Arena.IsOnPlatform(p.Position, p.BodyRadius);

        // Override grounding when being grapple-pulled
        if (p.Role == PawnRole.Grappler &&
            p.Hook.IsAttachedToWorld && p.Hook.ChainState == FistChainState.Retracting)
            p.IsGrounded = false;

        // Tick hook
        if (p.Role == PawnRole.Grappler)
            p.Hook.Tick(BeaconPhysics.FixedDt, p.Position);
    }

    private void CheckWinConditions()
    {
        if (Scores[0] >= TargetScore && Scores[1] >= TargetScore)
        {
            IsOver = true;
            WinnerTeam = Scores[0] > Scores[1] ? 0 : Scores[1] > Scores[0] ? 1 : -1;
        }
        else if (Scores[0] >= TargetScore)
        {
            IsOver = true;
            WinnerTeam = 0;
        }
        else if (Scores[1] >= TargetScore)
        {
            IsOver = true;
            WinnerTeam = 1;
        }
        else if (Tick >= MaxTicks)
        {
            // Overtime: extend if scores are within threshold
            float maxScore = Math.Max(Scores[0], Scores[1]);
            float minScore = Math.Min(Scores[0], Scores[1]);
            bool withinThreshold = maxScore > 0 && (maxScore - minScore) / maxScore <= OvertimeThreshold;
            bool isTied = Scores[0] == Scores[1];

            if ((withinThreshold || isTied) && MaxTicks < MaxOvertimeTicks)
            {
                MaxTicks += OvertimeExtension;
            }
            else
            {
                IsOver = true;
                WinnerTeam = Scores[0] > Scores[1] ? 0
                            : Scores[1] > Scores[0] ? 1
                            : -1;
            }
        }
    }
}
