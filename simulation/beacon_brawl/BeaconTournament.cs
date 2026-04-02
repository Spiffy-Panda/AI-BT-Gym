// ─────────────────────────────────────────────────────────────────────────────
// BeaconTournament.cs — Round-robin tournament for Beacon Brawl v2 teams
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using AiBtGym.BehaviorTree;

namespace AiBtGym.Simulation.BeaconBrawl;

/// <summary>A team entry: name, array of BTs (one per pawn), roles, optional color.</summary>
public record BeaconTeamEntry(string Name, List<BtNode>[] PawnTrees, PawnRole[] PawnRoles, string? Color = null);

public static class BeaconTournament
{
    public static GenerationSummary RunGeneration(
        List<BeaconTeamEntry> teams, int generation, string outputPath,
        int bestOf = 5, int? masterSeed = null, ArenaConfig? modifiers = null)
    {
        int seed = masterSeed ?? Environment.TickCount;
        var seedRng = new Random(seed);
        int n = teams.Count;
        string genDir = Path.Combine(outputPath, $"gen_{generation:D3}");

        // Create folder structure
        var teamDirs = new string[n];
        var teamIds = new string[n];
        for (int i = 0; i < n; i++)
        {
            teamIds[i] = $"team_{i:D2}";
            string folderName = $"{teamIds[i]}_{teams[i].Name}";
            teamDirs[i] = Path.Combine(genDir, "teams", folderName);
            Directory.CreateDirectory(Path.Combine(teamDirs[i], "battles"));
        }

        // Save initial BTs
        for (int i = 0; i < n; i++)
        {
            for (int p = 0; p < teams[i].PawnTrees.Length; p++)
            {
                string jsonPath = Path.Combine(teamDirs[i], $"pawn_{p}_bt.json");
                BtSerializer.SaveToFile(teams[i].PawnTrees[p], jsonPath);
            }
        }

        // Run round-robin
        float[] elos = new float[n];
        Array.Fill(elos, 1000f);
        int[] wins = new int[n], losses = new int[n], draws = new int[n];
        int[] scoreVictories = new int[n], timeoutWins = new int[n];
        var matchupResults = new List<MatchupResult>[n];
        var allBattleLogs = new List<BeaconBattleLog>[n];
        for (int i = 0; i < n; i++)
        {
            matchupResults[i] = [];
            allBattleLogs[i] = [];
        }

        int matchNum = 0;
        int matchesPlayed = 0;
        long totalDurationTicks = 0;
        int drawCount = 0;

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                matchNum++;

                for (int game = 0; game < bestOf; game++)
                {
                    int matchSeed = seedRng.Next();
                    string matchId = $"gen_{generation:D3}_match_{matchNum:D3}_g{game + 1}";

                    var arena = new BeaconArena(modifiers: modifiers);
                    var match = new BeaconMatch(arena,
                        teams[i].PawnTrees, teams[i].PawnRoles,
                        teams[j].PawnTrees, teams[j].PawnRoles,
                        seed: matchSeed);
                    var recorder = new BeaconRecorder(
                        [teams[i].Name, teams[j].Name],
                        generation, matchId,
                        [teams[i].Color, teams[j].Color],
                        teams[i].PawnTrees.Length);
                    match.Recorder = recorder;

                    while (!match.IsOver)
                        match.Step();

                    totalDurationTicks += match.Tick;
                    matchesPlayed++;

                    var replay = recorder.BuildReplayData(teams[i].PawnTrees, teams[j].PawnTrees, arena, matchSeed);
                    var logI = recorder.BuildBattleLog(0) with { Replay = replay };
                    var logJ = recorder.BuildBattleLog(1) with { Replay = replay };
                    allBattleLogs[i].Add(logI);
                    allBattleLogs[j].Add(logJ);

                    // Write battle log files
                    string fileNameI = $"match_{matchNum:D3}_g{game + 1}_vs_{teamIds[j]}.json";
                    string fileNameJ = $"match_{matchNum:D3}_g{game + 1}_vs_{teamIds[i]}.json";
                    WriteBattleLog(Path.Combine(teamDirs[i], "battles", fileNameI), logI);
                    WriteBattleLog(Path.Combine(teamDirs[j], "battles", fileNameJ), logJ);

                    // Update records
                    float scoreI;
                    if (match.WinnerTeam == 0)
                    {
                        wins[i]++; losses[j]++;
                        scoreI = 1f;
                        bool isScoreVictory = match.Scores[0] >= match.TargetScore;
                        if (isScoreVictory) scoreVictories[i]++;
                        else timeoutWins[i]++;
                    }
                    else if (match.WinnerTeam == 1)
                    {
                        wins[j]++; losses[i]++;
                        scoreI = 0f;
                        bool isScoreVictory = match.Scores[1] >= match.TargetScore;
                        if (isScoreVictory) scoreVictories[j]++;
                        else timeoutWins[j]++;
                    }
                    else
                    {
                        draws[i]++; draws[j]++;
                        scoreI = 0.5f;
                        drawCount++;
                    }

                    var (newI, newJ) = EloCalculator.Calculate(elos[i], elos[j], scoreI);
                    elos[i] = newI;
                    elos[j] = newJ;

                    MatchResult resI = scoreI == 1f ? MatchResult.Win : scoreI == 0f ? MatchResult.Loss : MatchResult.Draw;
                    MatchResult resJ = scoreI == 0f ? MatchResult.Win : scoreI == 1f ? MatchResult.Loss : MatchResult.Draw;
                    matchupResults[i].Add(new MatchupResult { Opponent = $"{teamIds[j]}_{teams[j].Name}", Result = resI, MatchFile = fileNameI });
                    matchupResults[j].Add(new MatchupResult { Opponent = $"{teamIds[i]}_{teams[i].Name}", Result = resJ, MatchFile = fileNameJ });
                }
            }
        }

        // Write team status files
        for (int i = 0; i < n; i++)
        {
            int played = wins[i] + losses[i] + draws[i];
            var logs = allBattleLogs[i];

            var status = new BeaconTeamStatus
            {
                TeamId = teamIds[i],
                Name = teams[i].Name,
                Color = teams[i].Color,
                Generation = generation,
                Record = new RecordData { Wins = wins[i], Losses = losses[i], Draws = draws[i], MatchesPlayed = played },
                Elo = elos[i],
                WinRate = played > 0 ? (float)wins[i] / played : 0,
                AggregateMetrics = new BeaconAggregateMetrics
                {
                    AvgMatchDurationTicks = logs.Count > 0 ? (float)logs.Average(l => l.DurationTicks) : 0,
                    AvgScoreDiff = logs.Count > 0 ? (float)logs.Average(l => l.FinalScores[0] - l.FinalScores[1]) : 0,
                    AvgBeaconOwnershipPct = logs.Count > 0 ? (float)logs.Average(l => l.BeaconControl.TotalBeaconOwnershipPct) : 0,
                    AvgKillsPerMatch = logs.Count > 0 ? (float)logs.Average(l => l.CombatSummary.KillsScored) : 0,
                    AvgDeathsPerMatch = logs.Count > 0 ? (float)logs.Average(l => l.CombatSummary.Deaths) : 0,
                    TotalScoreVictories = scoreVictories[i],
                    TotalTimeoutWins = timeoutWins[i]
                },
                MatchupResults = matchupResults[i]
            };

            string statusJson = JsonSerializer.Serialize(status, TournamentJson.Options);
            File.WriteAllText(Path.Combine(teamDirs[i], "status.json"), statusJson);
        }

        // Write generation summary
        var leaderboard = Enumerable.Range(0, n)
            .OrderByDescending(i => elos[i])
            .Select((idx, rank) => new LeaderboardEntry
            {
                Rank = rank + 1,
                FighterId = teamIds[idx],
                Name = teams[idx].Name,
                Color = teams[idx].Color,
                Elo = elos[idx],
                Record = $"{wins[idx]}-{losses[idx]}-{draws[idx]}"
            })
            .ToList();

        string dllHash = "";
        try
        {
            var asm = typeof(BeaconTournament).Assembly;
            string? dllPath = asm.Location;
            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
            {
                string projectDir = global::Godot.ProjectSettings.GlobalizePath("res://");
                dllPath = Path.Combine(projectDir, ".godot", "mono", "temp", "bin", "Debug", "AI-BT-Gym.dll");
            }
            if (File.Exists(dllPath))
            {
                var bytes = File.ReadAllBytes(dllPath);
                dllHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            }
        }
        catch { }

        var summary = new GenerationSummary
        {
            Generation = generation,
            Timestamp = DateTime.UtcNow.ToString("o"),
            FighterCount = n,
            TotalMatches = matchesPlayed,
            TournamentFormat = "round_robin_beacon_brawl_v2",
            BestOf = bestOf,
            Seed = seed,
            DllHash = dllHash,
            Leaderboard = leaderboard,
            MetaStats = new MetaStats
            {
                AvgMatchDurationTicks = matchesPlayed > 0 ? (float)totalDurationTicks / matchesPlayed : 0,
                KnockoutRate = 0,
                DrawRate = matchesPlayed > 0 ? (float)drawCount / matchesPlayed : 0
            }
        };

        string summaryJson = JsonSerializer.Serialize(summary, TournamentJson.Options);
        File.WriteAllText(Path.Combine(genDir, "generation_summary.json"), summaryJson);

        return summary;
    }

    /// <summary>
    /// Run a challenge tournament: one challenger team vs each opponent,
    /// rotating through the given map presets. No opponent-vs-opponent matches.
    /// </summary>
    public static GenerationSummary RunChallenge(
        BeaconTeamEntry challenger, List<BeaconTeamEntry> opponents,
        (string name, ArenaConfig? mods)[] mapPresets,
        int generation, string outputPath,
        int bestOf = 3, int? masterSeed = null)
    {
        // Build full team list: challenger first, then opponents
        var teams = new List<BeaconTeamEntry> { challenger };
        teams.AddRange(opponents);
        int n = teams.Count;

        int seed = masterSeed ?? Environment.TickCount;
        var seedRng = new Random(seed);
        string genDir = Path.Combine(outputPath, $"gen_{generation:D3}");

        var teamDirs = new string[n];
        var teamIds = new string[n];
        for (int i = 0; i < n; i++)
        {
            teamIds[i] = $"team_{i:D2}";
            string folderName = $"{teamIds[i]}_{teams[i].Name}";
            teamDirs[i] = Path.Combine(genDir, "teams", folderName);
            Directory.CreateDirectory(Path.Combine(teamDirs[i], "battles"));
        }

        // Save BTs
        for (int i = 0; i < n; i++)
            for (int p = 0; p < teams[i].PawnTrees.Length; p++)
                BtSerializer.SaveToFile(teams[i].PawnTrees[p], Path.Combine(teamDirs[i], $"pawn_{p}_bt.json"));

        float[] elos = new float[n];
        Array.Fill(elos, 1000f);
        int[] wins = new int[n], losses = new int[n], draws = new int[n];
        int[] scoreVictories = new int[n], timeoutWins = new int[n];
        var matchupResults = new List<MatchupResult>[n];
        var allBattleLogs = new List<BeaconBattleLog>[n];
        for (int i = 0; i < n; i++) { matchupResults[i] = []; allBattleLogs[i] = []; }

        int matchNum = 0;
        int matchesPlayed = 0;
        long totalDurationTicks = 0;
        int drawCount = 0;

        // Challenger (index 0) vs each opponent (index 1..n-1)
        for (int j = 1; j < n; j++)
        {
            matchNum++;

            for (int game = 0; game < bestOf; game++)
            {
                // Rotate through map presets
                var (mapName, mods) = mapPresets[game % mapPresets.Length];

                int matchSeed = seedRng.Next();
                string matchId = $"gen_{generation:D3}_match_{matchNum:D3}_g{game + 1}_{mapName}";

                var arena = new BeaconArena(modifiers: mods);
                var match = new BeaconMatch(arena,
                    teams[0].PawnTrees, teams[0].PawnRoles,
                    teams[j].PawnTrees, teams[j].PawnRoles,
                    seed: matchSeed);
                var recorder = new BeaconRecorder(
                    [teams[0].Name, teams[j].Name],
                    generation, matchId,
                    [teams[0].Color, teams[j].Color],
                    teams[0].PawnTrees.Length);
                match.Recorder = recorder;

                while (!match.IsOver)
                    match.Step();

                totalDurationTicks += match.Tick;
                matchesPlayed++;

                var replay = recorder.BuildReplayData(teams[0].PawnTrees, teams[j].PawnTrees, arena, matchSeed);
                var log0 = recorder.BuildBattleLog(0) with { Replay = replay };
                var logJ = recorder.BuildBattleLog(1) with { Replay = replay };
                allBattleLogs[0].Add(log0);
                allBattleLogs[j].Add(logJ);

                string fileName0 = $"match_{matchNum:D3}_g{game + 1}_{mapName}_vs_{teamIds[j]}.json";
                string fileNameJ = $"match_{matchNum:D3}_g{game + 1}_{mapName}_vs_{teamIds[0]}.json";
                WriteBattleLog(Path.Combine(teamDirs[0], "battles", fileName0), log0);
                WriteBattleLog(Path.Combine(teamDirs[j], "battles", fileNameJ), logJ);

                float scoreI;
                if (match.WinnerTeam == 0) { wins[0]++; losses[j]++; scoreI = 1f; scoreVictories[0]++; }
                else if (match.WinnerTeam == 1) { wins[j]++; losses[0]++; scoreI = 0f; scoreVictories[j]++; }
                else { draws[0]++; draws[j]++; scoreI = 0.5f; drawCount++; }

                var (new0, newJ) = EloCalculator.Calculate(elos[0], elos[j], scoreI);
                elos[0] = new0; elos[j] = newJ;

                MatchResult res0 = scoreI == 1f ? MatchResult.Win : scoreI == 0f ? MatchResult.Loss : MatchResult.Draw;
                MatchResult resJ = scoreI == 0f ? MatchResult.Win : scoreI == 1f ? MatchResult.Loss : MatchResult.Draw;
                matchupResults[0].Add(new MatchupResult { Opponent = $"{teamIds[j]}_{teams[j].Name}", Result = res0, MatchFile = fileName0 });
                matchupResults[j].Add(new MatchupResult { Opponent = $"{teamIds[0]}_{teams[0].Name}", Result = resJ, MatchFile = fileNameJ });
            }
        }

        // Write team status + generation summary (same format as RunGeneration)
        for (int i = 0; i < n; i++)
        {
            int played = wins[i] + losses[i] + draws[i];
            var logs = allBattleLogs[i];
            var status = new BeaconTeamStatus
            {
                TeamId = teamIds[i], Name = teams[i].Name, Color = teams[i].Color, Generation = generation,
                Record = new RecordData { Wins = wins[i], Losses = losses[i], Draws = draws[i], MatchesPlayed = played },
                Elo = elos[i], WinRate = played > 0 ? (float)wins[i] / played : 0,
                AggregateMetrics = new BeaconAggregateMetrics
                {
                    AvgMatchDurationTicks = logs.Count > 0 ? (float)logs.Average(l => l.DurationTicks) : 0,
                    AvgScoreDiff = logs.Count > 0 ? (float)logs.Average(l => l.FinalScores[0] - l.FinalScores[1]) : 0,
                    AvgBeaconOwnershipPct = logs.Count > 0 ? (float)logs.Average(l => l.BeaconControl.TotalBeaconOwnershipPct) : 0,
                    AvgKillsPerMatch = logs.Count > 0 ? (float)logs.Average(l => l.CombatSummary.KillsScored) : 0,
                    AvgDeathsPerMatch = logs.Count > 0 ? (float)logs.Average(l => l.CombatSummary.Deaths) : 0,
                    TotalScoreVictories = scoreVictories[i], TotalTimeoutWins = timeoutWins[i]
                },
                MatchupResults = matchupResults[i]
            };
            File.WriteAllText(Path.Combine(teamDirs[i], "status.json"), JsonSerializer.Serialize(status, TournamentJson.Options));
        }

        var leaderboard = Enumerable.Range(0, n)
            .OrderByDescending(i => elos[i])
            .Select((idx, rank) => new LeaderboardEntry
            {
                Rank = rank + 1, FighterId = teamIds[idx], Name = teams[idx].Name,
                Color = teams[idx].Color, Elo = elos[idx],
                Record = $"{wins[idx]}-{losses[idx]}-{draws[idx]}"
            }).ToList();

        string dllHash = "";
        try
        {
            var asm = typeof(BeaconTournament).Assembly;
            string? dllPath = asm.Location;
            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
                dllPath = Path.Combine(global::Godot.ProjectSettings.GlobalizePath("res://"), ".godot", "mono", "temp", "bin", "Debug", "AI-BT-Gym.dll");
            if (File.Exists(dllPath))
                dllHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dllPath))).ToLowerInvariant();
        } catch { }

        var summary = new GenerationSummary
        {
            Generation = generation, Timestamp = DateTime.UtcNow.ToString("o"),
            FighterCount = n, TotalMatches = matchesPlayed,
            TournamentFormat = "challenge_beacon_brawl_v2", BestOf = bestOf,
            Seed = seed, DllHash = dllHash, Leaderboard = leaderboard,
            MetaStats = new MetaStats
            {
                AvgMatchDurationTicks = matchesPlayed > 0 ? (float)totalDurationTicks / matchesPlayed : 0,
                KnockoutRate = 0, DrawRate = matchesPlayed > 0 ? (float)drawCount / matchesPlayed : 0
            }
        };
        File.WriteAllText(Path.Combine(genDir, "generation_summary.json"), JsonSerializer.Serialize(summary, TournamentJson.Options));
        return summary;
    }

    private static void WriteBattleLog(string path, BeaconBattleLog log)
    {
        string json = JsonSerializer.Serialize(log, TournamentJson.Options);
        File.WriteAllText(path, json);
    }
}
