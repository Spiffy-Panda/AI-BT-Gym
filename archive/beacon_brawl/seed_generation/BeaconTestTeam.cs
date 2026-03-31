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
// - Shrink awareness (stay within effective bounds)
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

    static BtNode C(string expr) => Cond(expr);

    /// <summary>Grappler: hazard-aware, pickup-hungry, generalist capper + brawler.</summary>
    public static List<BtNode> BuildGrappler() =>
    [
        Sel(
            HookStateMachine(),

            // Heal at base when critical
            HealAtBase(0.2f),

            // Reactive parry
            ParryReact(),

            // Exploit vulnerability windows
            ExploitVulnerable(),

            // Punish locked-out enemies
            PunishLockedOut(),

            // Engage enemies near beacons
            Seq("Engage near beacon", C("owned_beacon_count > 0"), C("nearest_enemy_dist < 400"),
                GrapplerEngage()),

            // Defend owned beacons
            DefendOwnedBeacon(),

            // Cap nearest side first
            CapNearestSideFirst(),

            // Push center when sides owned
            Seq("Push center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                ReachCenterPlatform()),

            // Platform dive
            GrapplerPlatformDive(),

            // Score-aware aggression
            ScoreAwarePush(),
            OffensiveRotation(),
            LateGamePush(),

            // Default: cap nearest unowned
            CapNearestUnowned(),
            Act("move_toward_nearest_unowned")
        )
    ];

    /// <summary>Gunner: hazard-aware, ranged pressure from safe positions.</summary>
    public static List<BtNode> BuildGunner() =>
    [
        Sel(
            // Heal at base when hurt
            HealAtBase(0.25f),

            // Parry if grappler gets close
            ParryReact(),

            // Exploit vulnerable targets
            ExploitVulnerable(),

            // Kite close enemies
            GunnerKite(250),

            // Snipe from range
            GunnerSnipe(800, 350),

            // Fire from beacon position
            Seq("Fire from beacon", C("in_beacon_left == 1 | in_beacon_right == 1"),
                GunnerPressure()),

            // Defend beacons
            DefendOwnedBeacon(),

            // Cap nearest
            CapNearestSideFirst(),
            CapNearestUnowned(),

            // Push center with mobility
            Seq("Help center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                GunnerMobility()),

            // Contest enemy beacons
            ContestBeacon(),
            LateGamePush(),

            // Default
            Act("move_toward_nearest_unowned")
        )
    ];
}
