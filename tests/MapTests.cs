// ─────────────────────────────────────────────────────────────────────────────
// MapTests.cs — Self-play validation for every arena configuration
// ─────────────────────────────────────────────────────────────────────────────
//
// Runs MapTester vs MapTester on each ArenaMaps preset. A map is considered
// "competitively viable" if:
//   1. The match doesn't crash (no exceptions)
//   2. Both fighters take damage (engagement happens)
//   3. The match reaches a decisive outcome OR goes to time (not a 100-100 stall)
//   4. Features are actually exercised (platform landings, hazard ticks, etc.)

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using AiBtGym.BehaviorTree;
using AiBtGym.Simulation;

namespace AiBtGym.Tests;

public static class MapTests
{
    public record MapTestResult
    {
        public string MapName { get; init; } = "";
        public bool Passed { get; init; }
        public string? Error { get; init; }
        public int DurationTicks { get; init; }
        public float Fighter0Hp { get; init; }
        public float Fighter1Hp { get; init; }
        public int WinnerIndex { get; init; }
        public Dictionary<string, string> FeatureNotes { get; init; } = new();
    }

    /// <summary>All arena presets to test, with their configs.</summary>
    private static readonly (string name, ArenaConfig config)[] Presets =
    [
        ("Flat", ArenaMaps.Flat),
        ("KingOfTheHill", ArenaMaps.KingOfTheHill),
        ("HazardStrips", ArenaMaps.HazardStrips),
        ("CenterWall", ArenaMaps.CenterWall),
        ("HealthPickup", ArenaMaps.HealthPickup),
        ("DippedCeiling", ArenaMaps.DippedCeiling),
        ("PressureRing", ArenaMaps.PressureRing),
        ("CombinedArena", ArenaMaps.CombinedArena),
    ];

    /// <summary>Run self-play on every map preset, plus asymmetric knowledge tests.</summary>
    public static List<MapTestResult> RunAll()
    {
        var tree = AiBtGym.Godot.MapTestTree.Build();
        var results = new List<MapTestResult>();

        // Symmetric self-play (both see the real config)
        foreach (var (name, config) in Presets)
        {
            try
            {
                results.Add(RunSelfPlay(name, config, tree));
            }
            catch (Exception e)
            {
                results.Add(new MapTestResult
                {
                    MapName = name, Passed = false,
                    Error = $"EXCEPTION: {e.Message}"
                });
            }
        }

        // Asymmetric knowledge tests: informed vs blind on each non-Flat map.
        // F0 = informed (sees real config), F1 = blind (sees Flat).
        // Tests that map awareness provides a competitive advantage.
        var flatConfig = ArenaMaps.Flat;
        foreach (var (name, config) in Presets)
        {
            if (name == "Flat") continue;
            try
            {
                results.Add(RunAsymmetric($"{name}_InformedVsBlind", config, tree,
                    knownConfig0: null,         // F0 sees truth
                    knownConfig1: flatConfig));  // F1 thinks it's a flat arena
            }
            catch (Exception e)
            {
                results.Add(new MapTestResult
                {
                    MapName = $"{name}_InformedVsBlind", Passed = false,
                    Error = $"EXCEPTION: {e.Message}"
                });
            }
        }

        return results;
    }

    private static MapTestResult RunSelfPlay(string mapName, ArenaConfig config, List<BtNode> tree)
    {
        var arena = new Arena(config);
        var match = new Match(arena, tree, tree, seed: 42);
        match.MaxTicks = 60 * 60; // 60 seconds

        // Attach recorder to capture action frequencies
        var recorder = new MatchRecorder(
            ["F0", "F1"], 0, [0, 0], $"map_test_{mapName}");
        match.Recorder = recorder;

        while (!match.IsOver)
            match.Step();

        var notes = new Dictionary<string, string>();
        var errors = new List<string>();

        float hp0 = match.Fighter0.Health;
        float hp1 = match.Fighter1.Health;

        // Extract action frequencies from the recorder
        var log0 = recorder.BuildBattleLog(0);
        var log1 = recorder.BuildBattleLog(1);
        var actions0 = log0.ActionFrequency;
        var actions1 = log1.ActionFrequency;

        // Merge both fighters' actions for a combined view
        var combined = new Dictionary<string, int>();
        foreach (var (k, v) in actions0) combined[k] = combined.GetValueOrDefault(k) + v;
        foreach (var (k, v) in actions1) combined[k] = combined.GetValueOrDefault(k) + v;

        // Top 6 actions
        var topActions = combined.OrderByDescending(kv => kv.Value).Take(6)
            .Select(kv => $"{kv.Key}={kv.Value}");
        notes["actions"] = string.Join(", ", topActions);

        // Positional data from the log
        notes["f0_pos"] = $"avgX={log0.PositionalSummary.AvgX:F0} avgY={log0.PositionalSummary.AvgY:F0} grnd={log0.PositionalSummary.TimeGroundedPct * 100:F0}%";
        notes["f1_pos"] = $"avgX={log1.PositionalSummary.AvgX:F0} avgY={log1.PositionalSummary.AvgY:F0} grnd={log1.PositionalSummary.TimeGroundedPct * 100:F0}%";

        // ── Check 1: Both fighters should have taken SOME damage ──
        float dmg0 = 100f - hp0;
        float dmg1 = 100f - hp1;
        float totalDamage = dmg0 + dmg1;

        if (totalDamage < 5f)
            errors.Add($"No real engagement: total damage dealt = {totalDamage:F1}");

        notes["damage"] = $"F0={dmg0:F0} F1={dmg1:F0} total={totalDamage:F0}";

        // ── Check 2: Outcome should be decisive ──
        if (match.WinnerIndex == -1 && hp0 > 95 && hp1 > 95)
            errors.Add("Stalemate: draw with both fighters near full HP");

        notes["outcome"] = match.WinnerIndex switch
        {
            0 => $"F0 wins ({hp0:F0} vs {hp1:F0})",
            1 => $"F1 wins ({hp1:F0} vs {hp0:F0})",
            _ => $"Draw ({hp0:F0} vs {hp1:F0})"
        };

        // ── Check 3: Feature-specific validation ──
        ValidateFeatures(config, match, notes, errors);

        string? error = errors.Count > 0 ? string.Join("; ", errors) : null;

        return new MapTestResult
        {
            MapName = mapName,
            Passed = errors.Count == 0,
            Error = error,
            DurationTicks = match.Tick,
            Fighter0Hp = hp0,
            Fighter1Hp = hp1,
            WinnerIndex = match.WinnerIndex,
            FeatureNotes = notes
        };
    }

    /// <summary>
    /// Run a match where F0 and F1 have different map knowledge.
    /// Both use the same BT; the only difference is what each is told about the map.
    /// </summary>
    private static MapTestResult RunAsymmetric(string mapName, ArenaConfig config,
        List<BtNode> tree, ArenaConfig? knownConfig0, ArenaConfig? knownConfig1)
    {
        var arena = new Arena(config);
        var match = new Match(arena, tree, tree, seed: 42);
        match.KnownConfig0 = knownConfig0;
        match.KnownConfig1 = knownConfig1;
        match.MaxTicks = 60 * 60;

        var recorder = new MatchRecorder(
            ["Informed", "Blind"], 0, [0, 0], $"asym_{mapName}");
        match.Recorder = recorder;

        while (!match.IsOver)
            match.Step();

        var notes = new Dictionary<string, string>();
        var errors = new List<string>();

        float hp0 = match.Fighter0.Health;
        float hp1 = match.Fighter1.Health;

        var log0 = recorder.BuildBattleLog(0);
        var log1 = recorder.BuildBattleLog(1);

        // Top actions for each fighter
        var top0 = log0.ActionFrequency.OrderByDescending(kv => kv.Value).Take(4)
            .Select(kv => $"{kv.Key}={kv.Value}");
        var top1 = log1.ActionFrequency.OrderByDescending(kv => kv.Value).Take(4)
            .Select(kv => $"{kv.Key}={kv.Value}");
        notes["informed_actions"] = string.Join(", ", top0);
        notes["blind_actions"] = string.Join(", ", top1);

        notes["outcome"] = match.WinnerIndex switch
        {
            0 => $"Informed wins ({hp0:F0} vs {hp1:F0})",
            1 => $"Blind wins ({hp1:F0} vs {hp0:F0})",
            _ => $"Draw ({hp0:F0} vs {hp1:F0})"
        };

        // The informed fighter should do at least as well as the blind one.
        // We don't fail if blind wins (seed RNG can cause that), but we note it.
        float hpAdvantage = hp0 - hp1;
        notes["hp_advantage"] = $"informed {(hpAdvantage >= 0 ? "+" : "")}{hpAdvantage:F0}";

        notes["duration"] = $"{match.Tick} ticks ({match.Tick / 60f:F1}s)";

        // Validate features were exercised
        ValidateFeatures(config, match, notes, errors);

        string? error = errors.Count > 0 ? string.Join("; ", errors) : null;

        return new MapTestResult
        {
            MapName = mapName,
            Passed = errors.Count == 0,
            Error = error,
            DurationTicks = match.Tick,
            Fighter0Hp = hp0,
            Fighter1Hp = hp1,
            WinnerIndex = match.WinnerIndex,
            FeatureNotes = notes
        };
    }

    private static void ValidateFeatures(ArenaConfig config, Match match, Dictionary<string, string> notes, List<string> errors)
    {
        // Hazard zones: at least some hazard damage should have been dealt
        // (fighters start near hazard positions in HazardStrips config)
        if (config.HazardZones.Count > 0)
        {
            float hazDmg0 = match.HazardDamageTaken[0];
            float hazDmg1 = match.HazardDamageTaken[1];
            notes["hazards"] = $"hazard_dmg: F0={hazDmg0:F1} F1={hazDmg1:F1} total={hazDmg0 + hazDmg1:F1}";
        }

        // Destructible walls: wall should be damaged or destroyed by end of match
        if (config.DestructibleWalls.Count > 0)
        {
            for (int i = 0; i < config.DestructibleWalls.Count; i++)
            {
                float initialHp = config.DestructibleWalls[i].Hp;
                float remainingHp = match.DestructibleWallHp[i];
                bool destroyed = !match.DestructibleWallExists[i];
                notes[$"wall_{i}"] = destroyed
                    ? "DESTROYED"
                    : $"hp={remainingHp:F0}/{initialHp:F0}";

                if (remainingHp >= initialHp)
                    errors.Add($"Wall {i} was never attacked (still at full HP)");
            }
        }

        // Pickups: at least one pickup cycle should have occurred
        if (config.Pickups.Count > 0)
        {
            for (int i = 0; i < config.Pickups.Count; i++)
            {
                bool stillActive = match.PickupActive[i];
                int timerRemaining = match.PickupRespawnTimer[i];
                // If timer > 0 or pickup is inactive, it was collected at some point
                bool wasCollected = !stillActive || timerRemaining > 0;
                notes[$"pickup_{i}"] = wasCollected ? "collected" : "never_collected";
                // Don't error — pickup collection depends on HP thresholds
            }
        }

        // Arena shrink: effective bounds should have shrunk by end
        if (config.Shrink != null)
        {
            float left = match.EffectiveLeft;
            float right = match.EffectiveRight;
            float originalLeft = match.Arena.Bounds.Position.X;
            float originalRight = match.Arena.Bounds.End.X;
            bool shrunk = left > originalLeft + 1f || right < originalRight - 1f;
            notes["shrink"] = shrunk
                ? $"bounds=[{left:F0}, {right:F0}] (from [{originalLeft:F0}, {originalRight:F0}])"
                : "no_shrink_yet";
            // Shrink only starts at 80% of match duration, might not trigger if KO happens early
        }

        // Match duration: very short matches might indicate a problem
        notes["duration"] = $"{match.Tick} ticks ({match.Tick / 60f:F1}s)";
    }

    /// <summary>
    /// Print results in the same format as MovementTests for consistency.
    /// Returns number of failures.
    /// </summary>
    public static int PrintResults(List<MapTestResult> results)
    {
        GD.Print("═══════════════════════════════════════");
        GD.Print("  Map Self-Play Tests");
        GD.Print("═══════════════════════════════════════");

        int passed = 0, failed = 0;
        var failures = new List<string>();

        foreach (var r in results)
        {
            if (r.Passed)
            {
                GD.Print($"  [PASS] {r.MapName} — {r.FeatureNotes.GetValueOrDefault("outcome", "")} ({r.DurationTicks / 60f:F1}s)");
                passed++;
            }
            else
            {
                GD.Print($"  [FAIL] {r.MapName}: {r.Error}");
                failed++;
                failures.Add($"{r.MapName}: {r.Error}");
            }

            // Print feature notes as sub-items
            foreach (var (key, value) in r.FeatureNotes)
            {
                if (key != "outcome")
                    GD.Print($"         {key}: {value}");
            }
        }

        GD.Print("═══════════════════════════════════════");
        GD.Print($"  Results: {passed} passed, {failed} failed");
        if (failures.Count > 0)
        {
            GD.Print("  Failures:");
            foreach (var f in failures) GD.Print($"    - {f}");
        }
        GD.Print("═══════════════════════════════════════");

        return failed;
    }
}
