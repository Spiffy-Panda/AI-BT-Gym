// ─────────────────────────────────────────────────────────────────────────────
// Tournament.cs — Round-robin tournament orchestrator for a generation
// ─────────────────────────────────────────────────────────────────────────────
//
// Usage:
//   var entries = Tournament.EntriesFromSeed(SeedTrees.Names, SeedTrees.All);
//   Tournament.RunGeneration(entries, generation: 0, outputPath: "generations");

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AiBtGym.BehaviorTree;

namespace AiBtGym.Simulation;

/// <summary>A fighter entry in a tournament: name, BT roots, and optional C# source.</summary>
public record TournamentEntry(string Name, List<BtNode> Roots, int BtVersion = 0, string? CSharpSource = null);

public static class Tournament
{
    /// <summary>Create tournament entries from parallel name/tree arrays (e.g. SeedTrees).</summary>
    public static List<TournamentEntry> EntriesFromSeed(string[] names, List<BtNode>[] trees)
    {
        var entries = new List<TournamentEntry>();
        for (int i = 0; i < names.Length; i++)
            entries.Add(new TournamentEntry(names[i], trees[i]));
        return entries;
    }

    /// <summary>
    /// Run a full round-robin tournament and write all output to disk.
    /// Returns the generation summary.
    /// </summary>
    public static GenerationSummary RunGeneration(
        List<TournamentEntry> entries, int generation, string outputPath)
    {
        int n = entries.Count;
        string genDir = Path.Combine(outputPath, $"gen_{generation:D3}");

        // ── Create folder structure ──
        var fighterDirs = new string[n];
        var fighterIds = new string[n];
        for (int i = 0; i < n; i++)
        {
            fighterIds[i] = $"fighter_{i:D2}";
            string folderName = $"{fighterIds[i]}_{entries[i].Name}";
            fighterDirs[i] = Path.Combine(genDir, "fighters", folderName);
            Directory.CreateDirectory(Path.Combine(fighterDirs[i], "battles"));
        }

        // ── Save initial BTs ──
        for (int i = 0; i < n; i++)
        {
            // JSONL log via TreeLog
            string treesPath = Path.Combine(fighterDirs[i], "trees.jsonl");
            TreeLog.Log(treesPath, entries[i].Name, entries[i].Roots,
                generation: generation,
                tags: new Dictionary<string, string> { ["origin"] = "seed" });

            // C# source file (if provided, or generate a stub)
            string csPath = Path.Combine(fighterDirs[i], $"bt_v{entries[i].BtVersion}.cs");
            if (entries[i].CSharpSource != null)
            {
                File.WriteAllText(csPath, entries[i].CSharpSource);
            }
            else
            {
                // Save serialized JSON as a reference alongside the JSONL
                string jsonPath = Path.Combine(fighterDirs[i], $"bt_v{entries[i].BtVersion}.json");
                BtSerializer.SaveToFile(entries[i].Roots, jsonPath);
            }
        }

        // ── Run round-robin matches ──
        float[] elos = new float[n];
        Array.Fill(elos, 1000f);
        int[] wins = new int[n], losses = new int[n], draws = new int[n];
        int[] knockouts = new int[n], timeoutsWon = new int[n], timeoutsLost = new int[n];
        var matchupResults = new List<MatchupResult>[n];
        var allBattleLogs = new List<BattleLog>[n];
        for (int i = 0; i < n; i++)
        {
            matchupResults[i] = [];
            allBattleLogs[i] = [];
        }

        int matchNum = 0;
        int totalMatches = n * (n - 1) / 2;
        int knockoutCount = 0;
        int drawCount = 0;
        long totalDurationTicks = 0;

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                matchNum++;
                string matchId = $"gen_{generation:D3}_match_{matchNum:D3}";

                // Create and run match
                var arena = new Arena();
                var match = new Match(arena, entries[i].Roots, entries[j].Roots);
                var recorder = new MatchRecorder(
                    [entries[i].Name, entries[j].Name],
                    generation,
                    [entries[i].BtVersion, entries[j].BtVersion],
                    matchId);
                match.Recorder = recorder;

                while (!match.IsOver)
                    match.Step();

                totalDurationTicks += match.Tick;

                // Build battle logs from both perspectives
                var logI = recorder.BuildBattleLog(0);
                var logJ = recorder.BuildBattleLog(1);
                allBattleLogs[i].Add(logI);
                allBattleLogs[j].Add(logJ);

                // Write battle log files
                string fileNameI = $"match_{matchNum:D3}_vs_{fighterIds[j]}.json";
                string fileNameJ = $"match_{matchNum:D3}_vs_{fighterIds[i]}.json";
                WriteBattleLog(Path.Combine(fighterDirs[i], "battles", fileNameI), logI);
                WriteBattleLog(Path.Combine(fighterDirs[j], "battles", fileNameJ), logJ);

                // Update records
                float scoreI;
                if (match.WinnerIndex == 0)
                {
                    wins[i]++; losses[j]++;
                    scoreI = 1f;
                    bool isKo = match.Fighter1.Health <= 0;
                    if (isKo) { knockouts[i]++; knockoutCount++; }
                    else { timeoutsWon[i]++; timeoutsLost[j]++; }
                }
                else if (match.WinnerIndex == 1)
                {
                    wins[j]++; losses[i]++;
                    scoreI = 0f;
                    bool isKo = match.Fighter0.Health <= 0;
                    if (isKo) { knockouts[j]++; knockoutCount++; }
                    else { timeoutsWon[j]++; timeoutsLost[i]++; }
                }
                else
                {
                    draws[i]++; draws[j]++;
                    scoreI = 0.5f;
                    drawCount++;
                }

                // Update ELO
                var (newI, newJ) = EloCalculator.Calculate(elos[i], elos[j], scoreI);
                elos[i] = newI;
                elos[j] = newJ;

                // Matchup results
                MatchResult resI = scoreI == 1f ? MatchResult.Win : scoreI == 0f ? MatchResult.Loss : MatchResult.Draw;
                MatchResult resJ = scoreI == 0f ? MatchResult.Win : scoreI == 1f ? MatchResult.Loss : MatchResult.Draw;
                matchupResults[i].Add(new MatchupResult
                {
                    Opponent = $"{fighterIds[j]}_{entries[j].Name}",
                    Result = resI,
                    MatchFile = fileNameI
                });
                matchupResults[j].Add(new MatchupResult
                {
                    Opponent = $"{fighterIds[i]}_{entries[i].Name}",
                    Result = resJ,
                    MatchFile = fileNameJ
                });
            }
        }

        // ── Write fighter status files ──
        for (int i = 0; i < n; i++)
        {
            int played = wins[i] + losses[i] + draws[i];
            var logs = allBattleLogs[i];

            var status = new FighterStatus
            {
                FighterId = fighterIds[i],
                Name = entries[i].Name,
                Generation = generation,
                BtVersion = entries[i].BtVersion,
                Record = new RecordData
                {
                    Wins = wins[i],
                    Losses = losses[i],
                    Draws = draws[i],
                    MatchesPlayed = played
                },
                Elo = elos[i],
                WinRate = played > 0 ? (float)wins[i] / played : 0,
                AggregateMetrics = new AggregateMetrics
                {
                    AvgDamageDealt = logs.Count > 0 ? logs.Average(l => l.DamageSummary.Dealt) : 0,
                    AvgDamageReceived = logs.Count > 0 ? logs.Average(l => l.DamageSummary.Received) : 0,
                    AvgMatchDurationTicks = logs.Count > 0 ? (float)logs.Average(l => l.DurationTicks) : 0,
                    AvgHitAccuracy = logs.Count > 0 ? (float)logs.Average(l => l.DamageSummary.HitAccuracy) : 0,
                    AvgTimeGroundedPct = logs.Count > 0 ? (float)logs.Average(l => l.PositionalSummary.TimeGroundedPct) : 0,
                    TotalKnockouts = knockouts[i],
                    TotalTimeoutsWon = timeoutsWon[i],
                    TotalTimeoutsLost = timeoutsLost[i]
                },
                MatchupResults = matchupResults[i]
            };

            string statusJson = JsonSerializer.Serialize(status, TournamentJson.Options);
            File.WriteAllText(Path.Combine(fighterDirs[i], "status.json"), statusJson);
        }

        // ── Write generation summary ──
        var leaderboard = Enumerable.Range(0, n)
            .OrderByDescending(i => elos[i])
            .Select((idx, rank) => new LeaderboardEntry
            {
                Rank = rank + 1,
                FighterId = fighterIds[idx],
                Name = entries[idx].Name,
                Elo = elos[idx],
                Record = $"{wins[idx]}-{losses[idx]}-{draws[idx]}"
            })
            .ToList();

        var summary = new GenerationSummary
        {
            Generation = generation,
            Timestamp = DateTime.UtcNow.ToString("o"),
            FighterCount = n,
            TotalMatches = totalMatches,
            TournamentFormat = "round_robin",
            Leaderboard = leaderboard,
            MetaStats = new MetaStats
            {
                AvgMatchDurationTicks = totalMatches > 0 ? (float)totalDurationTicks / totalMatches : 0,
                KnockoutRate = totalMatches > 0 ? (float)knockoutCount / totalMatches : 0,
                DrawRate = totalMatches > 0 ? (float)drawCount / totalMatches : 0
            }
        };

        string summaryJson = JsonSerializer.Serialize(summary, TournamentJson.Options);
        File.WriteAllText(Path.Combine(genDir, "generation_summary.json"), summaryJson);

        return summary;
    }

    private static void WriteBattleLog(string path, BattleLog log)
    {
        string json = JsonSerializer.Serialize(log, TournamentJson.Options);
        File.WriteAllText(path, json);
    }
}
