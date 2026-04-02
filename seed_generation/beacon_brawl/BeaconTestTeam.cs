// ─────────────────────────────────────────────────────────────────────────────
// BeaconTestTeam.cs — Adaptive test team for beacon brawl map/modifier testing
// ─────────────────────────────────────────────────────────────────────────────
//
// NOT part of the main tournament roster. Used for self-play validation to
// verify that modifiers (hazards, pickups, shrink, destructible walls, etc.)
// work correctly and that BTs can make use of them.
//
// The team is a generalist that reacts to any modifier combination:
// - Hazard avoidance (move off hazard zones)
// - Pickup collection (detour when hurt)
// - Wall destruction (break walls to open sightlines)
// - Standard beacon capture + defense + combat

using System.Collections.Generic;
using AiBtGym.BehaviorTree;
using AiBtGym.Simulation.BeaconBrawl;
using static AiBtGym.BehaviorTree.BtNode;
using static AiBtGym.Godot.BeaconSubTrees;

namespace AiBtGym.Godot;

public static class BeaconTestTeam
{
    public const string Name = "Map_Tester";
    public const string Color = "#95a5a6";

    public static BeaconTeamEntry GetEntry() => new(
        Name,
        [BuildGrappler(), BuildGunner()],
        [PawnRole.Grappler, PawnRole.Gunner],
        Color
    );

    /// <summary>
    /// Create a variant with selective map knowledge disabled.
    /// Use for informed-vs-uninformed experiments.
    /// </summary>
    public static BeaconTeamEntry GetEntry(string name, string color,
        bool usePickups = true, bool useHazards = true, bool useWalls = true) => new(
        name,
        [BuildGrappler(usePickups, useHazards, useWalls),
         BuildGunner(usePickups, useHazards, useWalls)],
        [PawnRole.Grappler, PawnRole.Gunner],
        color
    );

    static BtNode C(string expr) => Cond(expr);

    /// <summary>Grappler: hazard-aware, pickup-hungry, generalist capper + brawler.</summary>
    public static List<BtNode> BuildGrappler(bool usePickups = true, bool useHazards = true, bool useWalls = true)
    {
        var children = new List<BtNode>();
        children.Add(HookStateMachine());

        // Heal at base when critical (always available)
        children.Add(HealAtBase(0.2f));

        // ── Map awareness (after heal, before combat) ──
        if (useHazards) children.Add(AvoidHazard());
        if (usePickups) children.Add(SeekHealthPak());

        children.Add(ParryReact());
        children.Add(ExploitVulnerable());
        children.Add(PunishLockedOut());
        children.Add(Seq("Engage near beacon", C("owned_beacon_count > 0"), C("nearest_enemy_dist < 400"),
            GrapplerEngage()));

        if (useWalls) children.Add(BreakWall());

        children.Add(DefendOwnedBeacon());
        children.Add(CapNearestSideFirst());
        children.Add(Seq("Push center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
            ReachCenterPlatform()));
        children.Add(GrapplerPlatformDive());
        children.Add(ScoreAwarePush());
        children.Add(OffensiveRotation());
        children.Add(LateGamePush());
        children.Add(CapNearestUnowned());
        children.Add(Act("move_toward_nearest_unowned"));

        return [Sel(children.ToArray())];
    }

    /// <summary>Gunner: hazard-aware, ranged pressure from safe positions.</summary>
    public static List<BtNode> BuildGunner(bool usePickups = true, bool useHazards = true, bool useWalls = true)
    {
        var children = new List<BtNode>();

        // Heal at base when hurt (always available)
        children.Add(HealAtBase(0.25f));

        // ── Map awareness (after heal, before combat) ──
        if (useHazards) children.Add(AvoidHazard());
        if (usePickups) children.Add(SeekHealthPak());

        children.Add(ParryReact());
        children.Add(ExploitVulnerable());

        if (useWalls) children.Add(BreakWall());

        children.Add(GunnerKite(250));
        children.Add(GunnerSnipe(800, 350));
        children.Add(Seq("Fire from beacon", C("in_beacon_left == 1 | in_beacon_right == 1"),
            GunnerPressure()));
        children.Add(DefendOwnedBeacon());
        children.Add(CapNearestSideFirst());
        children.Add(CapNearestUnowned());
        children.Add(Seq("Help center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
            GunnerMobility()));
        children.Add(ContestBeacon());
        children.Add(LateGamePush());
        children.Add(Act("move_toward_nearest_unowned"));

        return [Sel(children.ToArray())];
    }
}
