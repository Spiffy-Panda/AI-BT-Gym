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

    private static void WriteBattleLog(string path, BeaconBattleLog log)
    {
        string json = JsonSerializer.Serialize(log, TournamentJson.Options);
        File.WriteAllText(path, json);
    }
}
