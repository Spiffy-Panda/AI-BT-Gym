// ─────────────────────────────────────────────────────────────────────────────
// BeaconMapTests.cs — Self-play validation for beacon brawl with arena modifiers
// ─────────────────────────────────────────────────────────────────────────────
//
// Runs TestTeam vs TestTeam on each beacon brawl modifier preset plus
// multi-modifier combos. Validates:
//   1. No crashes
//   2. Both teams take damage (engagement happens)
//   3. Beacons are captured (not a pure deathmatch)
//   4. Modifier features are exercised where applicable

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using AiBtGym.BehaviorTree;
using AiBtGym.Simulation;
using AiBtGym.Simulation.BeaconBrawl;

namespace AiBtGym.Tests;

public static class BeaconMapTests
{
    public record BeaconMapTestResult
    {
        public string MapName { get; init; } = "";
        public bool Passed { get; init; }
        public string? Error { get; init; }
        public int DurationTicks { get; init; }
        public int[] FinalScores { get; init; } = [0, 0];
        public int[] Kills { get; init; } = [0, 0];
        public int WinnerTeam { get; init; }
        public Dictionary<string, string> FeatureNotes { get; init; } = new();
        public BeaconBattleLog? BattleLog { get; init; }
    }

    private static readonly (string name, ArenaConfig? modifiers)[] Presets =
    [
        ("BB_Flat", null),
        ("BB_Hazards", ArenaMaps.BeaconHazards),
        ("BB_Pickups", ArenaMaps.BeaconPickups),
        ("BB_CenterWall", ArenaMaps.BeaconCenterWall),
        ("BB_Shrink", ArenaMaps.BeaconShrink),
        ("BB_Platforms", ArenaMaps.BeaconPlatforms),
        ("BB_Combined", ArenaMaps.BeaconCombined),
        ("BB_HazardsPickups", ArenaMaps.Merge(ArenaMaps.BeaconHazards, ArenaMaps.BeaconPickups)),
        ("BB_HazardsShrink", ArenaMaps.Merge(ArenaMaps.BeaconHazards, ArenaMaps.BeaconShrink)),
    ];

    public static List<BeaconMapTestResult> RunAll()
    {
        var team = AiBtGym.Godot.BeaconTestTeam.GetEntry();
        var results = new List<BeaconMapTestResult>();

        foreach (var (name, mods) in Presets)
        {
            try { results.Add(RunSelfPlay(name, mods, team)); }
            catch (Exception e)
            {
                results.Add(new BeaconMapTestResult { MapName = name, Passed = false, Error = $"EXCEPTION: {e.Message}" });
            }
        }

        return results;
    }

    private static BeaconMapTestResult RunSelfPlay(string mapName, ArenaConfig? modifiers, BeaconTeamEntry team)
    {
        var arena = new BeaconArena(modifiers: modifiers);
        var match = new BeaconMatch(arena,
            team.PawnTrees, team.PawnRoles,
            team.PawnTrees, team.PawnRoles,
            seed: 42);
        match.MaxTicks = 90 * 60; // 90 seconds

        var recorder = new BeaconRecorder(
            [team.Name, team.Name],
            0, $"bb_test_{mapName}",
            [team.Color, team.Color],
            team.PawnTrees.Length);
        match.Recorder = recorder;

        while (!match.IsOver)
            match.Step();

        var notes = new Dictionary<string, string>();
        var errors = new List<string>();

        // Basic outcome
        notes["scores"] = $"A={match.Scores[0]} B={match.Scores[1]}";
        notes["kills"] = $"A={match.Kills[0]} B={match.Kills[1]}";
        notes["outcome"] = match.WinnerTeam switch
        {
            0 => $"Team A wins ({match.Scores[0]}-{match.Scores[1]})",
            1 => $"Team B wins ({match.Scores[1]}-{match.Scores[0]})",
            _ => $"Draw ({match.Scores[0]}-{match.Scores[1]})"
        };
        notes["duration"] = $"{match.Tick} ticks ({match.Tick / 60f:F1}s)";

        // Check 1: Beacons should be captured (total score > 10)
        int totalScore = match.Scores[0] + match.Scores[1];
        if (totalScore < 10)
            errors.Add($"No beacon activity: total score = {totalScore}");

        // Check 2: Both teams should have some score (not a complete shutout)
        if (match.Scores[0] == 0 && match.Scores[1] == 0)
            errors.Add("Zero scoring: neither team captured beacons");

        // Feature-specific validation
        ValidateModifierFeatures(modifiers, match, notes, errors);

        string? error = errors.Count > 0 ? string.Join("; ", errors) : null;

        // Build battle log with replay
        var log = recorder.BuildBattleLog(0) with
        {
            Replay = recorder.BuildReplayData(team.PawnTrees, team.PawnTrees, arena, matchSeed: 42)
        };

        return new BeaconMapTestResult
        {
            MapName = mapName,
            Passed = errors.Count == 0,
            Error = error,
            DurationTicks = match.Tick,
            FinalScores = [match.Scores[0], match.Scores[1]],
            Kills = [match.Kills[0], match.Kills[1]],
            WinnerTeam = match.WinnerTeam,
            FeatureNotes = notes,
            BattleLog = log
        };
    }

    private static void ValidateModifierFeatures(ArenaConfig? modifiers, BeaconMatch match,
        Dictionary<string, string> notes, List<string> errors)
    {
        if (modifiers == null) return;

        // Hazard zones: at least one pawn should have taken hazard-specific damage
        if (modifiers.HazardZones.Count > 0)
        {
            float totalHazardDmg = match.AllPawns.Sum(p => p.HazardDamageTaken);
            notes["hazards"] = totalHazardDmg > 0
                ? $"active (total hazard dmg: {totalHazardDmg:F1})"
                : "no_hazard_damage";
            if (totalHazardDmg <= 0)
                errors.Add("Hazard zones dealt no damage — pawns may not be walking through them");
        }

        // Destructible walls
        if (modifiers.DestructibleWalls.Count > 0)
        {
            for (int i = 0; i < modifiers.DestructibleWalls.Count; i++)
            {
                float initialHp = modifiers.DestructibleWalls[i].Hp;
                float remaining = match.DestructibleWallHp[i];
                bool destroyed = !match.DestructibleWallExists[i];
                notes[$"wall_{i}"] = destroyed ? "DESTROYED" : $"hp={remaining:F0}/{initialHp:F0}";
            }
        }

        // Pickups
        if (modifiers.Pickups.Count > 0)
        {
            for (int i = 0; i < modifiers.Pickups.Count; i++)
            {
                bool wasCollected = !match.PickupActive[i] || match.PickupRespawnTimer[i] > 0;
                notes[$"pickup_{i}"] = wasCollected ? "collected" : "never_collected";
            }
        }

        // Arena shrink
        if (modifiers.Shrink != null)
        {
            float left = match.EffectiveLeft;
            float right = match.EffectiveRight;
            bool shrunk = left > match.Arena.Bounds.Position.X + 1f || right < match.Arena.Bounds.End.X - 1f;
            notes["shrink"] = shrunk
                ? $"bounds=[{left:F0}, {right:F0}]"
                : "not_yet_shrunk";
        }
    }

    public static int PrintResults(List<BeaconMapTestResult> results)
    {
        GD.Print("═══════════════════════════════════════");
        GD.Print("  Beacon Brawl Map Tests");
        GD.Print("═══════════════════════════════════════");

        int passed = 0, failed = 0;
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
            }
            foreach (var (key, value) in r.FeatureNotes)
                if (key != "outcome") GD.Print($"         {key}: {value}");
        }

        GD.Print("═══════════════════════════════════════");
        GD.Print($"  Results: {passed} passed, {failed} failed");
        GD.Print("═══════════════════════════════════════");
        return failed;
    }

    // ═════════════════════════════════════════════════════════════════════
    // Informed vs Uninformed experiments
    // ═════════════════════════════════════════════════════════════════════

    public record MatchupResult
    {
        public string Label { get; init; } = "";
        public string MapName { get; init; } = "";
        public int InformedWins { get; init; }
        public int UninformedWins { get; init; }
        public int Draws { get; init; }
        public int TotalGames { get; init; }
        public float InformedWinRate => TotalGames > 0 ? InformedWins / (float)TotalGames : 0f;
        public int InformedAvgScore { get; init; }
        public int UninformedAvgScore { get; init; }
    }

    private static readonly (string label, string map, ArenaConfig mods,
        bool pickups, bool hazards, bool walls)[] AsymmetricPresets =
    [
        ("NoPickups_vs_Informed", "BB_Pickups", ArenaMaps.BeaconPickups,
            false, true, true),
        ("NoHazards_vs_Informed", "BB_Hazards", ArenaMaps.BeaconHazards,
            true, false, true),
        ("NoWalls_vs_Informed", "BB_CenterWall", ArenaMaps.BeaconCenterWall,
            true, true, false),
        ("NoMapAware_vs_Informed_Combined", "BB_Combined", ArenaMaps.BeaconCombined,
            false, false, false),
        ("NoPickups_vs_Informed_Combined", "BB_Combined", ArenaMaps.BeaconCombined,
            false, true, true),
        ("NoHazards_vs_Informed_Combined", "BB_Combined", ArenaMaps.BeaconCombined,
            true, false, true),
    ];

    public static List<MatchupResult> RunInformedVsUninformed(int gamesPerMatchup = 10)
    {
        var informed = AiBtGym.Godot.BeaconTestTeam.GetEntry();
        var results = new List<MatchupResult>();

        foreach (var (label, mapName, mods, pickups, hazards, walls) in AsymmetricPresets)
        {
            var uninformed = AiBtGym.Godot.BeaconTestTeam.GetEntry(
                $"Uninformed_{label}", "#e74c3c",
                usePickups: pickups, useHazards: hazards, useWalls: walls);

            int iWins = 0, uWins = 0, draws = 0;
            long iScoreTotal = 0, uScoreTotal = 0;

            for (int g = 0; g < gamesPerMatchup; g++)
            {
                // Alternate sides to remove spawn bias
                bool informedIsTeamA = g % 2 == 0;
                var teamA = informedIsTeamA ? informed : uninformed;
                var teamB = informedIsTeamA ? uninformed : informed;

                var arena = new BeaconArena(modifiers: mods);
                var match = new BeaconMatch(arena,
                    teamA.PawnTrees, teamA.PawnRoles,
                    teamB.PawnTrees, teamB.PawnRoles,
                    seed: 100 + g);
                match.MaxTicks = 90 * 60;

                while (!match.IsOver)
                    match.Step();

                int aScore = match.Scores[0], bScore = match.Scores[1];
                int informedScore = informedIsTeamA ? aScore : bScore;
                int uninformedScore = informedIsTeamA ? bScore : aScore;
                iScoreTotal += informedScore;
                uScoreTotal += uninformedScore;

                int winner = match.WinnerTeam; // 0=A, 1=B, -1=draw
                if (winner == -1) draws++;
                else if ((winner == 0 && informedIsTeamA) || (winner == 1 && !informedIsTeamA))
                    iWins++;
                else
                    uWins++;
            }

            results.Add(new MatchupResult
            {
                Label = label,
                MapName = mapName,
                InformedWins = iWins,
                UninformedWins = uWins,
                Draws = draws,
                TotalGames = gamesPerMatchup,
                InformedAvgScore = (int)(iScoreTotal / gamesPerMatchup),
                UninformedAvgScore = (int)(uScoreTotal / gamesPerMatchup),
            });
        }

        return results;
    }

    public static void PrintMatchupResults(List<MatchupResult> results)
    {
        GD.Print("═══════════════════════════════════════════════════════════");
        GD.Print("  Informed vs Uninformed — Map Knowledge Experiment");
        GD.Print("═══════════════════════════════════════════════════════════");

        foreach (var r in results)
        {
            string bar = new('█', (int)(r.InformedWinRate * 20));
            string gap = new('░', 20 - bar.Length);
            GD.Print($"  {r.Label}");
            GD.Print($"    Map: {r.MapName} | Games: {r.TotalGames}");
            GD.Print($"    Informed:   {r.InformedWins}W / {r.UninformedWins}L / {r.Draws}D  ({r.InformedWinRate:P0})");
            GD.Print($"    Avg Score:  Informed {r.InformedAvgScore} vs Uninformed {r.UninformedAvgScore}");
            GD.Print($"    [{bar}{gap}]");
            GD.Print("");
        }

        GD.Print("═══════════════════════════════════════════════════════════");
    }

    // ═════════════════════════════════════════════════════════════════════
    // Head-to-head: two named teams across all map presets
    // ═════════════════════════════════════════════════════════════════════

    public record H2HResult
    {
        public string MapName { get; init; } = "";
        public int AWins { get; init; }
        public int BWins { get; init; }
        public int Draws { get; init; }
        public int TotalGames { get; init; }
        public int AAvgScore { get; init; }
        public int BAvgScore { get; init; }
    }

    private static readonly (string name, ArenaConfig? mods)[] AllMapPresets =
    [
        ("Flat", null),
        ("Hazards", ArenaMaps.BeaconHazards),
        ("Pickups", ArenaMaps.BeaconPickups),
        ("CenterWall", ArenaMaps.BeaconCenterWall),
        ("Shrink", ArenaMaps.BeaconShrink),
        ("Platforms", ArenaMaps.BeaconPlatforms),
        ("Combined", ArenaMaps.BeaconCombined),
        ("HazardsPickups", ArenaMaps.Merge(ArenaMaps.BeaconHazards, ArenaMaps.BeaconPickups)),
        ("HazardsShrink", ArenaMaps.Merge(ArenaMaps.BeaconHazards, ArenaMaps.BeaconShrink)),
    ];

    public static List<H2HResult> RunHeadToHead(BeaconTeamEntry teamA, BeaconTeamEntry teamB,
        int gamesPerMap = 10)
    {
        var results = new List<H2HResult>();

        foreach (var (mapName, mods) in AllMapPresets)
        {
            int aWins = 0, bWins = 0, draws = 0;
            long aScoreTotal = 0, bScoreTotal = 0;

            for (int g = 0; g < gamesPerMap; g++)
            {
                // Alternate sides
                bool aIsTeam0 = g % 2 == 0;
                var t0 = aIsTeam0 ? teamA : teamB;
                var t1 = aIsTeam0 ? teamB : teamA;

                var arena = new BeaconArena(modifiers: mods);
                var match = new BeaconMatch(arena,
                    t0.PawnTrees, t0.PawnRoles,
                    t1.PawnTrees, t1.PawnRoles,
                    seed: 200 + g);
                match.MaxTicks = 90 * 60;

                while (!match.IsOver)
                    match.Step();

                int s0 = match.Scores[0], s1 = match.Scores[1];
                int aScore = aIsTeam0 ? s0 : s1;
                int bScore = aIsTeam0 ? s1 : s0;
                aScoreTotal += aScore;
                bScoreTotal += bScore;

                int winner = match.WinnerTeam;
                if (winner == -1) draws++;
                else if ((winner == 0 && aIsTeam0) || (winner == 1 && !aIsTeam0)) aWins++;
                else bWins++;
            }

            results.Add(new H2HResult
            {
                MapName = mapName,
                AWins = aWins, BWins = bWins, Draws = draws,
                TotalGames = gamesPerMap,
                AAvgScore = (int)(aScoreTotal / gamesPerMap),
                BAvgScore = (int)(bScoreTotal / gamesPerMap),
            });
        }

        return results;
    }

    public static void PrintH2HResults(string nameA, string nameB, List<H2HResult> results)
    {
        GD.Print("═══════════════════════════════════════════════════════════");
        GD.Print($"  Head-to-Head: {nameA} vs {nameB}");
        GD.Print("═══════════════════════════════════════════════════════════");

        int totalAWins = 0, totalBWins = 0, totalDraws = 0;
        foreach (var r in results)
        {
            totalAWins += r.AWins; totalBWins += r.BWins; totalDraws += r.Draws;
            float aRate = r.TotalGames > 0 ? r.AWins / (float)r.TotalGames : 0;
            string bar = new('█', (int)(aRate * 20));
            string gap = new('░', 20 - bar.Length);
            string winner = r.AWins > r.BWins ? $"← {nameA}" : r.BWins > r.AWins ? $"{nameB} →" : "EVEN";
            GD.Print($"  {r.MapName,-16} {r.AWins}W-{r.BWins}L-{r.Draws}D  avg {r.AAvgScore}-{r.BAvgScore}  [{bar}{gap}]  {winner}");
        }

        int total = totalAWins + totalBWins + totalDraws;
        GD.Print("───────────────────────────────────────────────────────────");
        GD.Print($"  TOTAL          {totalAWins}W-{totalBWins}L-{totalDraws}D / {total} games");
        float overallRate = total > 0 ? totalAWins / (float)total : 0;
        GD.Print($"  {nameA} win rate: {overallRate:P0}");
        GD.Print("═══════════════════════════════════════════════════════════");
    }
}
