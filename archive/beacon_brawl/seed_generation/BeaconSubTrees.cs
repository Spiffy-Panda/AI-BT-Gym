// ─────────────────────────────────────────────────────────────────────────────
// BeaconSubTrees.cs — Shared behavior tree patterns for Beacon Brawl teams
// ─────────────────────────────────────────────────────────────────────────────
//
// Gen 6: Comprehensive rewrite for Season 2 balance:
// - 3x move/jump → faster engages, dodge-based play
// - Rifle charge delay (0.5s) → dodge window, aim commitment
// - Shorter pistol range → zone-control weapon, not snipe
// - One-sided platform edges → jump-through Mario style
// - Parry system fully integrated
// - Vulnerability exploitation (1.5x damage window)
// - Weapon lockout awareness (punish parried enemies)
// - Ally coordination patterns
// - Platform-aware positioning

using System;
using System.Collections.Generic;
using AiBtGym.BehaviorTree;
using static AiBtGym.BehaviorTree.BtNode;

namespace AiBtGym.Godot;

public static class BeaconSubTrees
{
    static BtNode C(string expr) => Cond(expr);

    public static readonly Dictionary<string, Func<List<BtNode>>> All = new()
    {
        ["HookStateMachine"] = () => [HookStateMachine()],
        ["HookMobility"] = () => [HookMobility()],
        ["MeleePunch"] = () => [MeleePunch()],
        ["GrapplerEngage"] = () => [GrapplerEngage()],
        ["HealAtBase"] = () => [HealAtBase()],
        ["CapNearestUnowned"] = () => [CapNearestUnowned()],
        ["GunnerSnipe"] = () => [GunnerSnipe()],
        ["GunnerMobility"] = () => [GunnerMobility()],
        ["GunnerKite"] = () => [GunnerKite()],
        ["ContestBeacon"] = () => [ContestBeacon()],
        ["CapNearestSideFirst"] = () => [CapNearestSideFirst()],
        ["DefendOwnedBeacon"] = () => [DefendOwnedBeacon()],
        ["ScoreAwarePush"] = () => [ScoreAwarePush()],
        ["ReachCenterPlatform"] = () => [ReachCenterPlatform()],
        ["OffensiveRotation"] = () => [OffensiveRotation()],
        ["LateGamePush"] = () => [LateGamePush()],
        ["ParryReact"] = () => [ParryReact()],
        ["ExploitVulnerable"] = () => [ExploitVulnerable()],
        ["PunishLockedOut"] = () => [PunishLockedOut()],
        ["GunnerPressure"] = () => [GunnerPressure()],
        ["GrapplerPlatformDive"] = () => [GrapplerPlatformDive()],
    };

    // ═════════════════════════════════════════════════════════════════════
    // GRAPPLER PATTERNS
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Hook state machine — manage lock/retract/detach cycle.
    /// </summary>
    public static BtNode HookStateMachine(int lockChain = 60) =>
        Sel("Hook state machine",
            Seq("Detach unanchored lock", C("hook_state == 2"), C("hook_attached == 0"), Act("detach_hook")),
            Seq("Retract anchored hook", C("hook_attached == 1"), Act("retract_hook")),
            Seq("Lock extending hook", C("hook_state == 1"), C($"hook_chain > {lockChain}"), Act("lock_hook"))
        ) with { SubTree = "HookStateMachine" };

    /// <summary>
    /// Hook-based mobility to reach specific beacons.
    /// </summary>
    public static BtNode HookMobility(string target = "center") =>
        Sel("Hook mobility",
            target switch
            {
                "left" => Seq("Hook to left", C("hook_ready == 1"), C("dist_to_beacon_left > 150"), Act("launch_hook_toward_beacon_left")),
                "right" => Seq("Hook to right", C("hook_ready == 1"), C("dist_to_beacon_right > 150"), Act("launch_hook_toward_beacon_right")),
                _ => Seq("Hook to center", C("hook_ready == 1"), C("dist_to_beacon_center > 150"), Act("launch_hook_toward_center")),
            }
        ) with { SubTree = "HookMobility" };

    /// <summary>
    /// Reach center platform: jump through one-sided edges, then hook up.
    /// With 3x jump the platform is directly reachable via jump alone near edges.
    /// </summary>
    public static BtNode ReachCenterPlatform() =>
        Sel("Reach center platform",
            // Jump up through one-sided platform edges (Mario-style)
            Seq("Jump through edge", C("beacon_center_owner != 1"), C("dist_to_beacon_center < 200"),
                C("is_grounded == 1"), Act("jump")),
            // Hook to center while airborne for final pull
            Seq("Hook center airborne", C("beacon_center_owner != 1"), C("dist_to_beacon_center < 250"),
                C("is_grounded == 0"), C("hook_ready == 1"), Act("launch_hook_toward_center")),
            // Walk toward center when far
            Seq("Walk to center", C("beacon_center_owner != 1"), C("dist_to_beacon_center > 200"),
                Act("move_toward_beacon_center"))
        ) with { SubTree = "ReachCenterPlatform" };

    /// <summary>
    /// Simple melee punch when very close.
    /// </summary>
    public static BtNode MeleePunch() =>
        Seq("Melee punch", C("nearest_enemy_dist < 40"), C("fist_ready == 1"), Act("punch_toward_enemy"))
        with { SubTree = "MeleePunch" };

    /// <summary>
    /// Grappler engage: hook toward enemy for gap-close, punch on arrival.
    /// Season 2: faster movement means walk-chase is viable at closer range.
    /// </summary>
    public static BtNode GrapplerEngage(int hookRange = 350) =>
        Sel("Grappler engage",
            // Punch if already close
            MeleePunch(),
            // Hook toward enemy for gap-close
            Seq("Hook chase", C($"nearest_enemy_dist < {hookRange}"), C("nearest_enemy_dist > 60"),
                C("hook_ready == 1"), Act("launch_hook_toward_enemy")),
            // Walk chase — viable now with 3x move speed
            Seq("Walk chase", C("nearest_enemy_dist < 250"), Act("move_toward_nearest_enemy"))
        ) with { SubTree = "GrapplerEngage" };

    /// <summary>
    /// Grappler dive from platform: jump down + hook enemy for aerial engage.
    /// Use the platform height advantage to dive onto enemies below.
    /// </summary>
    public static BtNode GrapplerPlatformDive() =>
        Sel("Platform dive",
            // On platform with enemy below — jump off and hook them
            Seq("Dive hook", C("in_beacon_center == 1"), C("nearest_enemy_dist < 400"),
                C("nearest_enemy_dir_y == 1"), C("hook_ready == 1"), Act("launch_hook_toward_enemy")),
            // On platform — punch anyone who jumps up
            MeleePunch()
        ) with { SubTree = "GrapplerPlatformDive" };

    /// <summary>
    /// Heal at base when low HP. Base regen is 10 HP/s.
    /// </summary>
    public static BtNode HealAtBase(float threshold = 0.25f) =>
        Seq("Heal at base", C($"my_health_pct < {threshold}"), Act("move_toward_base"))
        with { SubTree = "HealAtBase" };

    // ═════════════════════════════════════════════════════════════════════
    // DEFENSIVE PATTERNS (SHARED)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parry incoming melee — activate when enemy grappler is very close.
    /// Successful parry locks out the attacker's weapon for 2 seconds.
    /// </summary>
    public static BtNode ParryReact() =>
        Sel("Parry react",
            // Parry when enemy grappler in punch range
            Seq("Parry melee", C("parry_ready == 1"), C("nearest_enemy_dist < 50"), Act("activate_parry")),
            // Parry when about to be hooked (close range approach)
            Seq("Parry hook", C("parry_ready == 1"), C("nearest_enemy_dist < 80"),
                C("nearest_enemy_dist > 40"), Act("activate_parry"))
        ) with { SubTree = "ParryReact" };

    /// <summary>
    /// Exploit vulnerable enemies (1.5x damage multiplier).
    /// After a successful hook grab, enemy is vulnerable for 1 second.
    /// </summary>
    public static BtNode ExploitVulnerable() =>
        Sel("Exploit vulnerable",
            // Grappler: close and punch during vulnerability window
            Seq("Punish vulnerable melee", C("nearest_enemy_vulnerable == 1"),
                C("nearest_enemy_dist < 60"), C("fist_ready == 1"), Act("punch_toward_enemy")),
            // Gunner: prioritize rifle on vulnerable target (1.5x = 27 damage!)
            Seq("Rifle vulnerable", C("nearest_enemy_vulnerable == 1"),
                C("rifle_ready == 1"), Act("shoot_rifle_at_enemy")),
            // Chase vulnerable enemy
            Seq("Chase vulnerable", C("nearest_enemy_vulnerable == 1"),
                C("nearest_enemy_dist < 300"), Act("move_toward_nearest_enemy"))
        ) with { SubTree = "ExploitVulnerable" };

    /// <summary>
    /// Punish weapon-locked enemies (parried → 2s window of no weapons).
    /// Safe to go aggressive since they can't fight back.
    /// </summary>
    public static BtNode PunishLockedOut() =>
        Sel("Punish locked out",
            Seq("Rush locked enemy", C("nearest_enemy_weapon_locked == 1"),
                C("nearest_enemy_dist < 400"), Act("move_toward_nearest_enemy")),
            // Grappler: free punches
            Seq("Free punches", C("nearest_enemy_weapon_locked == 1"),
                C("nearest_enemy_dist < 40"), C("fist_ready == 1"), Act("punch_toward_enemy"))
        ) with { SubTree = "PunishLockedOut" };

    // ═════════════════════════════════════════════════════════════════════
    // BEACON CONTROL PATTERNS
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Cap nearest unowned beacon.
    /// </summary>
    public static BtNode CapNearestUnowned() =>
        Seq("Cap nearest unowned", C("owned_beacon_count < 3"), Act("move_toward_nearest_unowned"))
        with { SubTree = "CapNearestUnowned" };

    /// <summary>
    /// Cap nearest side beacon first (spawn-aware), then the far side.
    /// </summary>
    public static BtNode CapNearestSideFirst() =>
        Sel("Cap nearest side first",
            Seq("Cap near side", C("dist_to_beacon_left < dist_to_beacon_right"),
                C("beacon_left_owner != 1"), Act("move_toward_beacon_left")),
            Seq("Cap near side", C("dist_to_beacon_right <= dist_to_beacon_left"),
                C("beacon_right_owner != 1"), Act("move_toward_beacon_right")),
            Seq("Cap far side", C("beacon_left_owner != 1"), Act("move_toward_beacon_left")),
            Seq("Cap far side", C("beacon_right_owner != 1"), Act("move_toward_beacon_right"))
        ) with { SubTree = "CapNearestSideFirst" };

    /// <summary>
    /// Defend owned beacons when enemies enter them.
    /// Prioritizes center (3x value) over sides.
    /// </summary>
    public static BtNode DefendOwnedBeacon() =>
        Sel("Defend owned beacon",
            Seq("Defend center", C("beacon_center_owner == 1"), C("enemy_count_center_beacon > 0"),
                Act("move_toward_beacon_center")),
            Seq("Defend left", C("beacon_left_owner == 1"), C("enemy_count_left_beacon > 0"),
                Act("move_toward_beacon_left")),
            Seq("Defend right", C("beacon_right_owner == 1"), C("enemy_count_right_beacon > 0"),
                Act("move_toward_beacon_right"))
        ) with { SubTree = "DefendOwnedBeacon" };

    /// <summary>
    /// Contest enemy beacons to freeze their scoring. Center first (3x value).
    /// </summary>
    public static BtNode ContestBeacon() =>
        Sel("Contest enemy beacons",
            Seq("Contest center", C("beacon_center_owner == 2"), Act("move_toward_beacon_center")),
            Seq("Contest left", C("beacon_left_owner == 2"), Act("move_toward_beacon_left")),
            Seq("Contest right", C("beacon_right_owner == 2"), Act("move_toward_beacon_right"))
        ) with { SubTree = "ContestBeacon" };

    /// <summary>
    /// Offensive rotation: attack enemy beacons BEFORE defending own.
    /// </summary>
    public static BtNode OffensiveRotation() =>
        Sel("Offensive rotation",
            ContestBeacon(),
            DefendOwnedBeacon(),
            CapNearestUnowned()
        ) with { SubTree = "OffensiveRotation" };

    /// <summary>
    /// Score-aware behavior: adapt strategy based on score difference.
    /// </summary>
    public static BtNode ScoreAwarePush() =>
        Sel("Score-aware push",
            // Far behind: go for kills to create cap windows
            Seq("Way behind: hunt", C("score_diff < -10"), C("nearest_enemy_dist < 400"),
                Act("move_toward_nearest_enemy")),
            // Behind: contest to freeze enemy scoring
            Seq("Behind: contest", C("score_diff < -5"), C("enemy_beacon_count > 0"),
                Act("move_toward_nearest_contested")),
            // Ahead: play defensively, hold beacons
            Seq("Ahead: hold", C("score_diff > 10"), C("owned_beacon_count >= 2"),
                DefendOwnedBeacon())
        ) with { SubTree = "ScoreAwarePush" };

    /// <summary>
    /// Late-game push: break stalemates when scores are close.
    /// </summary>
    public static BtNode LateGamePush() =>
        Sel("Late game push",
            Seq("Tied: flip beacon", C("score_diff < 3"), C("score_diff > -3"),
                C("enemy_beacon_count > 0"), Act("move_toward_nearest_contested")),
            Seq("Losing: chase kill", C("score_diff < -3"), C("nearest_enemy_dist < 400"),
                Act("move_toward_nearest_enemy"))
        ) with { SubTree = "LateGamePush" };

    // ═════════════════════════════════════════════════════════════════════
    // GUNNER PATTERNS
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gunner sniping. Rifle has 0.5s charge — use at range to give time for shot.
    /// Pistol is short-range zone pressure (outer beacon distance).
    /// </summary>
    public static BtNode GunnerSnipe(int rifleRange = 800, int pistolRange = 350) =>
        Sel("Gunner snipe",
            // Priority: rifle vulnerable targets (1.5x = 27 damage)
            Seq("Rifle vulnerable", C("nearest_enemy_vulnerable == 1"), C("rifle_ready == 1"),
                Act("shoot_rifle_at_enemy")),
            // Rifle at medium-long range (charge delay = they can dodge, so shoot early)
            Seq("Rifle at range", C($"nearest_enemy_dist < {rifleRange}"), C("nearest_enemy_dist > 200"),
                C("rifle_ready == 1"), Act("shoot_rifle_at_enemy")),
            // Pistol for close-medium zone pressure
            Seq("Pistol pressure", C($"nearest_enemy_dist < {pistolRange}"), C("pistol_ready == 1"),
                Act("shoot_pistol_at_enemy"))
        ) with { SubTree = "GunnerSnipe" };

    /// <summary>
    /// Gunner sustained pressure: keep shooting while holding position.
    /// Good for beacon defense — maintain fire while standing on point.
    /// </summary>
    public static BtNode GunnerPressure() =>
        Sel("Gunner pressure",
            Seq("Rifle while holding", C("nearest_enemy_dist < 900"), C("rifle_ready == 1"),
                Act("shoot_rifle_at_enemy")),
            Seq("Pistol while holding", C("nearest_enemy_dist < 350"), C("pistol_ready == 1"),
                Act("shoot_pistol_at_enemy")),
            // Wall bounce when enemy is behind cover or at center
            Seq("Ricochet shot", C("nearest_enemy_dist < 900"), C("rifle_ready == 1"),
                C("nearest_enemy_dir_y == -1"), Act("shoot_rifle_at_wall_bounce"))
        ) with { SubTree = "GunnerPressure" };

    /// <summary>
    /// Gunner mobility via rifle recoil for platform access.
    /// Jump + shoot_rifle_down = upward boost to reach center platform.
    /// </summary>
    public static BtNode GunnerMobility() =>
        Sel("Gunner mobility",
            Seq("Jump for rifle boost", C("is_grounded == 1"), C("dist_to_beacon_center < 300"),
                C("beacon_center_owner != 1"), Act("jump")),
            Seq("Rifle jump to center", C("is_grounded == 0"), C("dist_to_beacon_center < 300"),
                C("beacon_center_owner != 1"), C("rifle_ready == 1"), Act("shoot_rifle_down")),
            // General movement boost when far from objectives
            Seq("Rifle dash", C("is_grounded == 1"), C("nearest_unowned_dist > 500"),
                C("rifle_ready == 1"), Act("shoot_rifle_down"))
        ) with { SubTree = "GunnerMobility" };

    /// <summary>
    /// Kiting pattern: shoot then retreat to maintain distance.
    /// With faster movement, retreat is more effective.
    /// </summary>
    public static BtNode GunnerKite(int retreatDist = 200) =>
        Sel("Gunner kite",
            // Parry if enemy gets too close
            Seq("Parry close melee", C($"nearest_enemy_dist < 50"), C("parry_ready == 1"),
                Act("activate_parry")),
            // Shoot at close range
            Seq("Rifle kite", C($"nearest_enemy_dist < {retreatDist}"), C("rifle_ready == 1"),
                Act("shoot_rifle_at_enemy")),
            Seq("Pistol kite", C($"nearest_enemy_dist < {retreatDist}"), C("pistol_ready == 1"),
                Act("shoot_pistol_at_enemy")),
            // Retreat when weapons on cooldown
            Seq("Retreat on cooldown", C($"nearest_enemy_dist < {retreatDist}"),
                C("rifle_ready == 0"), C("pistol_ready == 0"), Act("move_away_from_nearest_enemy"))
        ) with { SubTree = "GunnerKite" };
}
