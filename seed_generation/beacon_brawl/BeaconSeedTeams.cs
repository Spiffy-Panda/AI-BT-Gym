// ─────────────────────────────────────────────────────────────────────────────
// BeaconSeedTeams.cs — 4 seed Grappler+Gunner teams for Beacon Brawl v2
// ─────────────────────────────────────────────────────────────────────────────
//
// Gen 6: Full rewrite for Season 2 balance:
// - 3x move/jump, -600 jump impulse
// - Rifle: 2x range, 0.5s charge delay (dodge window)
// - Pistol: shorter range (outer beacon zone distance)
// - Platform: one-sided edges (jump-through from below)
//
// All teams now use:
// - Parry system (melee defense + weapon lockout)
// - Vulnerability exploitation (1.5x damage after hook grab)
// - Weapon lockout punishment (free hits when enemy parried)
// - Platform strategy (jump-through edges, height advantage)
// - Score awareness (adapt tactics based on scoreboard)

using System.Collections.Generic;
using AiBtGym.BehaviorTree;
using AiBtGym.Simulation.BeaconBrawl;
using static AiBtGym.BehaviorTree.BtNode;
using static AiBtGym.Godot.BeaconSubTrees;

namespace AiBtGym.Godot;

public static class BeaconSeedTeams
{
    public static readonly string[] Names =
    [
        "Iron_Vanguard",
        "Shadow_Flankers",
        "Bastion_Guard",
        "Blitz_Runners"
    ];

    public static readonly string[] HexColors =
    [
        "#c0392b", // Iron red
        "#8e44ad", // Shadow purple
        "#2980b9", // Bastion blue
        "#f39c12"  // Blitz gold
    ];

    public static List<BeaconTeamEntry> GetAllEntries()
    {
        var allTrees = All;
        var entries = new List<BeaconTeamEntry>();
        for (int i = 0; i < Names.Length; i++)
            entries.Add(new BeaconTeamEntry(Names[i], allTrees[i].Trees, allTrees[i].Roles, HexColors[i]));
        return entries;
    }

    public record TeamDef(List<BtNode>[] Trees, PawnRole[] Roles);

    public static readonly TeamDef[] All =
    [
        IronVanguard(),
        ShadowFlankers(),
        BastionGuard(),
        BlitzRunners()
    ];

    static BtNode C(string expr) => Cond(expr);

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Iron_Vanguard — "Parry Brawler"
    //    Grappler uses parry aggressively to lock out enemies, then punishes.
    //    Gunner provides sustained fire support from beacon positions.
    //    Strategy: control through combat superiority, not positioning.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef IronVanguard() => new(
    [
        // Pawn 0: Grappler — parry-focused brawler
        [Sel(
            HookStateMachine(),
            HealAtBase(0.2f),

            // Reactive parry when enemy closes in
            ParryReact(),

            // Exploit vulnerability windows (1.5x damage)
            ExploitVulnerable(),

            // Punish weapon-locked enemies (free hits after parry)
            PunishLockedOut(),

            // Engage enemies near our beacons
            Seq("Engage near beacon", C("owned_beacon_count > 0"), C("nearest_enemy_dist < 400"),
                GrapplerEngage()),

            DefendOwnedBeacon(),
            CapNearestSideFirst(),

            // Push center — jump through platform edges
            Seq("Push center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                ReachCenterPlatform()),

            // Platform dive when holding center
            GrapplerPlatformDive(),

            ScoreAwarePush(),
            OffensiveRotation(),
            LateGamePush(),
            CapNearestUnowned(),
            Act("move_toward_nearest_unowned")
        )],

        // Pawn 1: Gunner — beacon holder with sustained fire
        [Sel(
            HealAtBase(0.25f),

            // Parry if enemy grappler gets close
            ParryReact(),

            // Exploit vulnerable targets with rifle (27 damage!)
            ExploitVulnerable(),

            // Kite close enemies
            GunnerKite(250),

            // Sustained pressure while holding beacon position
            Seq("Fire from beacon", C("in_beacon_left == 1 | in_beacon_right == 1"),
                GunnerPressure()),

            // Standard sniping
            GunnerSnipe(800, 350),

            DefendOwnedBeacon(),
            CapNearestUnowned(),

            // Center access
            Seq("Reach center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                GunnerMobility()),

            OffensiveRotation(),
            LateGamePush(),
            Act("move_toward_nearest_unowned")
        )]
    ],
    [PawnRole.Grappler, PawnRole.Gunner]);

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Shadow_Flankers — "Split Map Control"
    //    Both pawns cap nearest side simultaneously, then converge center.
    //    Gunner uses wall bounces to harass center from ground level.
    //    Strategy: fast beacon lock → scoring lead → defend with combat.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef ShadowFlankers() => new(
    [
        // Pawn 0: Grappler — fast cap + center dive
        [Sel(
            HookStateMachine(),
            HealAtBase(0.3f),

            ParryReact(),
            ExploitVulnerable(),

            // Defend contested beacons with engagement
            Seq("Defend with engage", C("owned_beacon_count > 0"), C("nearest_enemy_dist < 300"),
                GrapplerEngage()),

            DefendOwnedBeacon(),
            CapNearestSideFirst(),

            // Jump through platform to center
            Seq("Push center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                ReachCenterPlatform()),

            // Dive from platform to contest ground beacons
            Seq("Platform dive defend", C("in_beacon_center == 1"), C("enemy_count_left_beacon > 0 | enemy_count_right_beacon > 0"),
                Sel(
                    Seq(C("enemy_count_left_beacon > 0"), Act("move_toward_beacon_left")),
                    Seq(C("enemy_count_right_beacon > 0"), Act("move_toward_beacon_right"))
                )),

            ContestBeacon(),
            LateGamePush(),
            Act("move_toward_nearest_unowned")
        )],

        // Pawn 1: Gunner — zone controller with ricochet
        [Sel(
            HealAtBase(0.25f),
            ParryReact(),
            ExploitVulnerable(),

            GunnerKite(250),

            // Wall bounce to hit enemies at center from ground
            Seq("Ricochet center", C("enemy_count_center_beacon > 0"), C("rifle_ready == 1"),
                Act("shoot_rifle_at_wall_bounce")),

            GunnerSnipe(800, 350),

            DefendOwnedBeacon(),
            CapNearestUnowned(),

            // Help center with rifle boost
            Seq("Help center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                GunnerMobility()),

            // Pressure from beacon while holding
            Seq("Hold and shoot", C("in_beacon_left == 1 | in_beacon_right == 1"),
                C("nearest_enemy_dist < 900"), GunnerPressure()),

            LateGamePush(),
            Act("move_toward_nearest_unowned")
        )]
    ],
    [PawnRole.Grappler, PawnRole.Gunner]);

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Bastion_Guard — "Center Fortress"
    //    Grappler holds center platform (3x value), gunner caps sides.
    //    Grappler uses height advantage for dive attacks and hook denials.
    //    Strategy: center is worth 3x → holding it wins on points alone.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef BastionGuard() => new(
    [
        // Pawn 0: Grappler — center platform king
        [Sel(
            HookStateMachine(),
            HealAtBase(0.2f),

            ParryReact(),
            ExploitVulnerable(),
            PunishLockedOut(),

            // Dive attack from platform
            Seq("Platform engage", C("in_beacon_center == 1"), C("nearest_enemy_dist < 400"),
                GrapplerPlatformDive()),

            // Engage enemies contesting center
            Seq("Engage center", C("dist_to_beacon_center < 200"), C("nearest_enemy_dist < 300"),
                GrapplerEngage()),

            // Defend center when owned (high priority since 3x)
            Seq("Defend center", C("beacon_center_owner == 1"), C("enemy_count_center_beacon > 0"),
                Act("move_toward_beacon_center")),

            // Rush center — jump through platform edges
            ReachCenterPlatform(),

            // Hold center
            Seq("Hold center", C("in_beacon_center == 1"), Act("move_toward_beacon_center")),

            // Help cap sides only when center is secure and empty of enemies
            Seq("Help sides", C("beacon_center_owner == 1"), C("enemy_count_center_beacon == 0"),
                CapNearestUnowned()),

            LateGamePush(),
            Act("move_toward_beacon_center")
        )],

        // Pawn 1: Gunner — side capper + fire support
        [Sel(
            HealAtBase(0.3f),
            ParryReact(),
            ExploitVulnerable(),

            GunnerKite(250),
            GunnerSnipe(800, 350),

            DefendOwnedBeacon(),
            CapNearestSideFirst(),

            // Fire support for center from ground
            Seq("Support center", C("beacon_center_owner == 1"), C("enemy_count_center_beacon > 0"),
                C("rifle_ready == 1"), Act("shoot_rifle_at_wall_bounce")),

            // Help center with rifle boost if sides owned
            Seq("Help center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                GunnerMobility()),

            ContestBeacon(),
            LateGamePush(),
            Act("move_toward_nearest_unowned")
        )]
    ],
    [PawnRole.Grappler, PawnRole.Gunner]);

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Blitz_Runners — "Kill Snowball"
    //    Grappler hunts kills aggressively. Kills give +5 score AND create
    //    respawn windows (3s) for free beacon flipping.
    //    Gunner finishes low-HP targets and caps during respawn windows.
    //    Strategy: kill pressure → score from kills + cap during respawn.
    // ═══════════════════════════════════════════════════════════════════════
    public static TeamDef BlitzRunners() => new(
    [
        // Pawn 0: Grappler — aggressive hunter
        [Sel(
            HookStateMachine(),
            HealAtBase(0.15f), // Lower threshold — stay aggressive longer

            // Parry for self-defense
            ParryReact(),

            // Always exploit vulnerability — these are kill windows
            ExploitVulnerable(),

            // Punish locked-out enemies (free damage after parry)
            PunishLockedOut(),

            // Hunt enemies aggressively
            Seq("Hunt enemies", C("nearest_enemy_dist < 500"),
                GrapplerEngage()),

            // Cap during enemy respawn (3 second window)
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),
            Seq("Cap both dead", C("enemy2_dead == 1"),
                Act("move_toward_nearest_unowned")),

            DefendOwnedBeacon(),
            CapNearestSideFirst(),

            // Push center for kills (enemies often cap center)
            Seq("Push center", C("enemy_count_center_beacon > 0"),
                ReachCenterPlatform()),

            ContestBeacon(),
            LateGamePush(),
            CapNearestUnowned(),
            Act("move_toward_nearest_enemy") // Always hunting
        )],

        // Pawn 1: Gunner — finisher + opportunistic capper
        [Sel(
            HealAtBase(0.2f),
            ParryReact(),

            // Exploit vulnerable enemies (rifle = 27 damage on vulnerable!)
            ExploitVulnerable(),

            GunnerKite(200),

            // Finish low enemies for +5 kill bonus
            Seq("Finish low rifle", C("enemy_health_pct < 0.3"), C("rifle_ready == 1"),
                Act("shoot_rifle_at_enemy")),
            Seq("Finish low pistol", C("enemy_health_pct < 0.3"), C("nearest_enemy_dist < 350"),
                C("pistol_ready == 1"), Act("shoot_pistol_at_enemy")),
            Seq("Chase low HP", C("enemy_health_pct < 0.3"), C("nearest_enemy_dist < 300"),
                Act("move_toward_nearest_enemy")),

            GunnerSnipe(800, 350),

            // Cap during respawn windows
            Seq("Cap during respawn", C("enemy_dead == 1"),
                Act("move_toward_nearest_unowned")),

            DefendOwnedBeacon(),
            CapNearestUnowned(),

            // Push center with mobility
            Seq("Push center", C("owned_beacon_count >= 2"), C("beacon_center_owner != 1"),
                GunnerMobility()),

            LateGamePush(),
            Act("move_toward_nearest_unowned")
        )]
    ],
    [PawnRole.Grappler, PawnRole.Gunner]);
}
