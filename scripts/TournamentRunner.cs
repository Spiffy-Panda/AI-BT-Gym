// ─────────────────────────────────────────────────────────────────────────────
// TournamentRunner.cs — Headless Godot script to run a seed tournament
// ─────────────────────────────────────────────────────────────────────────────
//
// Run: godot_console.exe --headless --scene res://scenes/tournament_runner.tscn

using System;
using System.IO;
using Godot;
using AiBtGym.Simulation;

namespace AiBtGym.Godot;

public partial class TournamentRunner : Node
{
    public override void _Ready()
    {
        GD.Print("═══════════════════════════════════════");
        GD.Print("  Tournament Runner");
        GD.Print("═══════════════════════════════════════");

        string outputPath = Path.Combine(
            ProjectSettings.GlobalizePath("res://"), "generations");

        var entries = Tournament.EntriesFromSeed(SeedTrees.Names, SeedTrees.All);
        GD.Print($"  Fighters: {entries.Count}");
        GD.Print($"  Matches:  {entries.Count * (entries.Count - 1) / 2}");
        GD.Print($"  Output:   {outputPath}");
        GD.Print("───────────────────────────────────────");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var summary = Tournament.RunGeneration(entries, generation: 0, outputPath: outputPath);
        stopwatch.Stop();

        GD.Print($"  Completed in {stopwatch.ElapsedMilliseconds}ms");
        GD.Print("");
        GD.Print("  Leaderboard:");
        foreach (var entry in summary.Leaderboard)
        {
            GD.Print($"    #{entry.Rank}  {entry.Name,-28} ELO {entry.Elo,7:F1}  {entry.Record}");
        }
        GD.Print("");
        GD.Print($"  Avg match duration: {summary.MetaStats.AvgMatchDurationTicks:F0} ticks ({summary.MetaStats.AvgMatchDurationTicks / 60f:F1}s)");
        GD.Print($"  Knockout rate:      {summary.MetaStats.KnockoutRate * 100:F0}%");
        GD.Print($"  Draw rate:          {summary.MetaStats.DrawRate * 100:F0}%");
        GD.Print("═══════════════════════════════════════");

        GetTree().Quit(0);
    }
}
