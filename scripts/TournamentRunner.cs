// ─────────────────────────────────────────────────────────────────────────────
// TournamentRunner.cs — HTTP server for running tournaments and viewing results
// ─────────────────────────────────────────────────────────────────────────────
//
// Run: godot_console.exe --headless --scene res://scenes/tournament_runner.tscn
// Then open http://localhost:8585 in a browser.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using AiBtGym.BehaviorTree;
using AiBtGym.Simulation;
using AiBtGym.Simulation.BeaconBrawl;
using AiBtGym.Tests;

namespace AiBtGym.Godot;

public partial class TournamentRunner : Node
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private string _outputPath = "";
    private string _projectDir = "";
    private readonly HashSet<string> _runningTournaments = new();
    private readonly object _runningLock = new();
    private readonly Dictionary<string, GenerationSummary> _lastSummaries = new();
    private List<TestResult>? _lastTestResults;
    private List<MapTests.MapTestResult>? _lastMapTestResults;
    private List<BeaconMapTests.BeaconMapTestResult>? _lastBbMapTestResults;
    private List<BeaconMapTests.MatchupResult>? _lastMatchupResults;
    private DateTime? _testsRanAt;
    private string _mapTestDir = "";
    private string _bbMapTestDir = "";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public override void _Ready()
    {
        _projectDir = ProjectSettings.GlobalizePath("res://");
        _outputPath = Path.Combine(_projectDir, "generations");
        _mapTestDir = Path.Combine(_projectDir, "map_test_replays");
        _bbMapTestDir = Path.Combine(_projectDir, "bb_map_test_replays");

        var port = System.Environment.GetEnvironmentVariable("PORT") ?? "8585";
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _cts = new CancellationTokenSource();

        try
        {
            _listener.Start();
            GD.Print("═══════════════════════════════════════");
            GD.Print("  Tournament Server");
            GD.Print($"  http://localhost:{port}");
            GD.Print("═══════════════════════════════════════");
            _ = ListenLoop(_cts.Token);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to start HTTP server: {ex.Message}");
            GetTree().Quit(1);
        }
    }

    public override void _ExitTree()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();
    }

    private async System.Threading.Tasks.Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener!.IsListening)
        {
            try
            {
                var ctx = await _listener.GetContextAsync();
                _ = HandleRequest(ctx);
            }
            catch (ObjectDisposedException) { break; }
            catch (HttpListenerException) { break; }
        }
    }

    private async System.Threading.Tasks.Task HandleRequest(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        var path = req.Url?.AbsolutePath ?? "/";
        var method = req.HttpMethod;

        try
        {
            res.Headers.Add("Access-Control-Allow-Origin", "*");
            res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (method == "OPTIONS")
            {
                res.StatusCode = 204;
                res.Close();
                return;
            }

            if (path == "/" && method == "GET")
                await ServeDashboard(res);
            else if (path == "/replay" && method == "GET")
                await ServeReplayViewer(res);
            else if (path == "/tests" && method == "GET")
                await ServeTestPage(res);
            else if (path == "/bt-compare" && method == "GET")
                await ServeBtCompare(res);
            else if (path == "/screenshots" && method == "GET")
                await ServeScreenshotsPage(res);
            else if (path == "/api/screenshots" && method == "GET")
                await ServeScreenshotsList(res);
            else if (path.StartsWith("/api/screenshots/img/") && method == "GET")
                await ServeScreenshotImage(res, path);
            else if (path == "/api/screenshots/clear" && method == "POST")
                await ClearScreenshots(res);
            else if (path == "/api/replay/launch" && method == "POST")
                await LaunchReplay(req, res);
            else if (path == "/api/tests/run" && method == "POST")
                await RunTests(res);
            else if (path == "/api/tests/status" && method == "GET")
                await ServeJson(res, new { ran_at = _testsRanAt?.ToString("o") });
            else if (path == "/api/tests/results" && method == "GET")
                await ServeJson(res, _lastTestResults ?? new List<TestResult>());
            else if (path == "/api/tests/map-results" && method == "GET")
                await ServeMapTestResults(res);
            else if (path == "/api/tests/bb-map-results" && method == "GET")
                await ServeBbMapTestResults(res);
            else if (path == "/api/tests/bb-map-launch" && method == "POST")
                await LaunchBbMapTestReplay(req, res);
            else if (path == "/api/tests/map-launch" && method == "POST")
                await LaunchMapTestReplay(req, res);

            // ── Tournament list ──
            else if (path == "/api/tournaments" && method == "GET")
                await ServeTournamentList(res);

            // ── Tournament-scoped routes: /api/tournaments/{tid}/... ──
            else if (path.StartsWith("/api/tournaments/") && method == "GET")
            {
                var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                // parts: ["api", "tournaments", "{tid}", ...]
                if (parts.Length >= 3)
                {
                    var tid = parts[2];
                    if (parts.Length == 3)
                        await ServeTournamentInfo(res, tid);
                    else if (parts.Length >= 4 && parts[3] == "generations")
                    {
                        var outPath = GetTournamentOutputPath(tid);
                        if (parts.Length == 4)
                            await ServeGenerations(res, outPath);
                        else
                            await ServeGenerationData(res, path, outPath, genPartIndex: 4);
                    }
                    else if (parts.Length >= 4 && parts[3] == "status")
                        await ServeTournamentStatus(res, tid);
                    else
                    { res.StatusCode = 404; await WriteText(res, "Not found"); }
                }
                else { res.StatusCode = 400; await WriteText(res, "Bad request"); }
            }
            else if (path.StartsWith("/api/tournaments/") && method == "POST")
            {
                var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 && parts[3] == "run")
                    await RunTournament(req, res, parts[2]);
                else
                { res.StatusCode = 404; await WriteText(res, "Not found"); }
            }

            // ── Legacy routes (default tournament) ──
            else if (path == "/api/status" && method == "GET")
                await ServeTournamentStatus(res, "default");
            else if (path == "/api/tournament/run" && method == "POST")
                await RunTournament(req, res, "default");
            else if (path == "/api/generations" && method == "GET")
                await ServeGenerations(res, _outputPath);
            else if (path.StartsWith("/api/generations/") && method == "GET")
                await ServeGenerationData(res, path, _outputPath, genPartIndex: 2)
            ;
            else
            {
                res.StatusCode = 404;
                await WriteText(res, "Not found");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Request error: {ex.Message}");
            try
            {
                res.StatusCode = 500;
                await WriteText(res, $"Error: {ex.Message}");
            }
            catch { res.Abort(); }
        }
    }

    // ── API Handlers ──

    private string GetTournamentOutputPath(string tournamentId)
    {
        if (tournamentId == "default") return _outputPath;
        return Path.Combine(_projectDir, "tournaments", tournamentId);
    }

    private async System.Threading.Tasks.Task ServeTournamentList(HttpListenerResponse res)
    {
        var tournaments = new List<object>();
        foreach (var config in TournamentRegistry.GetAll())
        {
            var outPath = GetTournamentOutputPath(config.Id);
            int genCount = 0;
            int latestGen = -1;
            if (Directory.Exists(outPath))
            {
                var dirs = Directory.GetDirectories(outPath, "gen_*");
                genCount = dirs.Length;
                if (dirs.Length > 0)
                    latestGen = dirs.Select(d => int.TryParse(Path.GetFileName(d).Replace("gen_", ""), out int n) ? n : -1).Max();
            }
            bool running;
            lock (_runningLock) { running = _runningTournaments.Contains(config.Id); }
            tournaments.Add(new
            {
                id = config.Id,
                displayName = config.DisplayName,
                generationCount = genCount,
                latestGeneration = latestGen,
                running
            });
        }
        await ServeJson(res, tournaments);
    }

    private async System.Threading.Tasks.Task ServeTournamentInfo(HttpListenerResponse res, string tournamentId)
    {
        var config = TournamentRegistry.Get(tournamentId);
        if (config == null) { res.StatusCode = 404; await WriteText(res, "Tournament not found"); return; }
        var outPath = GetTournamentOutputPath(tournamentId);
        int genCount = 0;
        if (Directory.Exists(outPath))
            genCount = Directory.GetDirectories(outPath, "gen_*").Length;
        bool running;
        lock (_runningLock) { running = _runningTournaments.Contains(tournamentId); }
        await ServeJson(res, new { id = config.Id, displayName = config.DisplayName, generationCount = genCount, running });
    }

    private async System.Threading.Tasks.Task ServeTournamentStatus(HttpListenerResponse res, string tournamentId)
    {
        bool running;
        lock (_runningLock) { running = _runningTournaments.Contains(tournamentId); }
        _lastSummaries.TryGetValue(tournamentId, out var lastSummary);
        await ServeJson(res, new { tournamentRunning = running, lastSummary });
    }

    private async System.Threading.Tasks.Task RunTournament(HttpListenerRequest req, HttpListenerResponse res, string tournamentId)
    {
        var config = TournamentRegistry.Get(tournamentId);
        if (config == null) { res.StatusCode = 404; await ServeJson(res, new { error = "Unknown tournament: " + tournamentId }); return; }

        lock (_runningLock)
        {
            if (_runningTournaments.Contains(tournamentId))
            {
                res.StatusCode = 409;
                _ = ServeJson(res, new { error = $"Tournament '{tournamentId}' already running" });
                return;
            }
            _runningTournaments.Add(tournamentId);
        }

        var outPath = GetTournamentOutputPath(tournamentId);

        // Determine generation number
        int gen = 0;
        if (Directory.Exists(outPath))
        {
            var dirs = Directory.GetDirectories(outPath, "gen_*");
            if (dirs.Length > 0)
                gen = dirs.Select(d => int.TryParse(Path.GetFileName(d).Replace("gen_", ""), out int n) ? n : -1).Max() + 1;
        }

        GD.Print($"[{tournamentId}] Starting generation {gen}...");

        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            GenerationSummary summary;

            if (config.GameType == TournamentGameType.BeaconBrawl && config.GetBeaconTeams != null)
            {
                var teamEntries = config.GetBeaconTeams(gen);
                summary = BeaconTournament.RunGeneration(teamEntries, generation: gen, outputPath: outPath);
            }
            else
            {
                var contestants = config.GetContestants(gen);
                var entries = Tournament.EntriesFromSeed(contestants.Names, contestants.Trees, contestants.HexColors);
                summary = Tournament.RunGeneration(entries, generation: gen, outputPath: outPath);
            }
            stopwatch.Stop();

            _lastSummaries[tournamentId] = summary;
            lock (_runningLock) { _runningTournaments.Remove(tournamentId); }

            GD.Print($"[{tournamentId}]   Gen {gen} completed in {stopwatch.ElapsedMilliseconds}ms");
            foreach (var entry in summary.Leaderboard)
                GD.Print($"    #{entry.Rank}  {entry.Name,-28} ELO {entry.Elo,7:F1}  {entry.Record}");

            await ServeJson(res, new
            {
                tournament = tournamentId,
                generation = gen,
                elapsedMs = stopwatch.ElapsedMilliseconds,
                summary
            });
        }
        catch (Exception ex)
        {
            lock (_runningLock) { _runningTournaments.Remove(tournamentId); }
            res.StatusCode = 500;
            await ServeJson(res, new { error = ex.Message });
        }
    }

    private async System.Threading.Tasks.Task ServeGenerations(HttpListenerResponse res, string outputPath)
    {
        var gens = new List<object>();
        if (Directory.Exists(outputPath))
        {
            foreach (var dir in Directory.GetDirectories(outputPath, "gen_*").OrderBy(d => d))
            {
                var summaryPath = Path.Combine(dir, "generation_summary.json");
                if (File.Exists(summaryPath))
                {
                    var json = File.ReadAllText(summaryPath);
                    var summary = JsonSerializer.Deserialize<GenerationSummary>(json, TournamentJson.Options);
                    if (summary != null) gens.Add(summary);
                }
            }
        }
        await ServeJson(res, gens);
    }

    private async System.Threading.Tasks.Task ServeGenerationData(HttpListenerResponse res, string path, string outputPath, int genPartIndex)
    {
        // Generic handler for generation data routes.
        // genPartIndex is the index in parts[] where the generation ID appears.
        // For legacy: /api/generations/{id}/... → genPartIndex=2
        // For scoped: /api/tournaments/{tid}/generations/{id}/... → genPartIndex=4
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length <= genPartIndex) { res.StatusCode = 400; await WriteText(res, "Bad request"); return; }

        // Handle "sub-trees" pseudo-generation
        if (parts[genPartIndex] == "sub-trees")
        {
            // Remap parts so ServeSubTreeData sees ["api", "generations", "sub-trees", ...]
            var remapped = new[] { "api", "generations" }.Concat(parts.Skip(genPartIndex)).ToArray();
            await ServeSubTreeData(res, remapped);
            return;
        }

        string genDir = Path.Combine(outputPath, $"gen_{int.Parse(parts[genPartIndex]):D3}");
        if (!Directory.Exists(genDir)) { res.StatusCode = 404; await WriteText(res, "Generation not found"); return; }

        int fightersIdx = genPartIndex + 1;

        if (parts.Length == genPartIndex + 1)
        {
            // Generation summary
            var summaryPath = Path.Combine(genDir, "generation_summary.json");
            if (File.Exists(summaryPath))
                await ServeRawJson(res, File.ReadAllText(summaryPath));
            else
            { res.StatusCode = 404; await WriteText(res, "Summary not found"); }
            return;
        }

        if (parts.Length >= fightersIdx + 1 && parts[fightersIdx] == "fighters")
        {
            // Support both "fighters" (fight mode) and "teams" (beacon brawl) directory names
            var fightersDir = Path.Combine(genDir, "fighters");
            if (!Directory.Exists(fightersDir))
                fightersDir = Path.Combine(genDir, "teams");
            if (!Directory.Exists(fightersDir)) { res.StatusCode = 404; await WriteText(res, "No fighters"); return; }

            int fidIdx = fightersIdx + 1;

            if (parts.Length == fightersIdx + 1)
            {
                // List fighters/teams — serve raw JSON objects to support both FighterStatus and BeaconTeamStatus
                var items = new List<Dictionary<string, object?>>();
                foreach (var dir in Directory.GetDirectories(fightersDir).OrderBy(d => d))
                {
                    var statusPath = Path.Combine(dir, "status.json");
                    if (File.Exists(statusPath))
                    {
                        var json = File.ReadAllText(statusPath);
                        var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, TournamentJson.Options);
                        if (dict != null)
                        {
                            // Normalize: if team_id exists but fighter_id doesn't, alias it
                            if (dict.ContainsKey("team_id") && !dict.ContainsKey("fighter_id"))
                                dict["fighter_id"] = dict["team_id"];
                            items.Add(dict);
                        }
                    }
                }
                await ServeJson(res, items);
                return;
            }

            // Find fighter dir by partial match
            string fighterId = parts[fidIdx];
            var fighterDir = Directory.GetDirectories(fightersDir)
                .FirstOrDefault(d => Path.GetFileName(d).StartsWith(fighterId));
            if (fighterDir == null) { res.StatusCode = 404; await WriteText(res, "Fighter not found"); return; }

            int subIdx = fidIdx + 1;

            if (parts.Length == fidIdx + 1)
            {
                // Fighter/team status — inject fighter_id alias if needed
                var statusPath = Path.Combine(fighterDir, "status.json");
                if (File.Exists(statusPath))
                {
                    var json = File.ReadAllText(statusPath);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(json, TournamentJson.Options);
                    if (dict != null && dict.ContainsKey("team_id") && !dict.ContainsKey("fighter_id"))
                        dict["fighter_id"] = dict["team_id"];
                    await ServeJson(res, dict);
                }
                else
                { res.StatusCode = 404; await WriteText(res, "Status not found"); }
                return;
            }

            if (parts.Length >= subIdx + 1 && parts[subIdx] == "bt")
            {
                // Try standard fight BT first, then beacon brawl pawn BTs
                var btPath = Path.Combine(fighterDir, "bt_v0.json");
                if (File.Exists(btPath))
                {
                    await ServeRawJson(res, File.ReadAllText(btPath));
                }
                else
                {
                    // Beacon brawl: wrap each pawn BT in a labeled Selector so the
                    // tree viewer renders them as top-level sections.
                    var pawnBts = Directory.GetFiles(fighterDir, "pawn_*_bt.json").OrderBy(f => f).ToArray();
                    if (pawnBts.Length > 0)
                    {
                        var combined = new List<object>();
                        for (int pi = 0; pi < pawnBts.Length; pi++)
                        {
                            var pawnNodes = JsonSerializer.Deserialize<List<object>>(
                                File.ReadAllText(pawnBts[pi]), TournamentJson.Options) ?? [];
                            // Detect role by scanning for hook/rifle actions in the tree JSON
                            var btText = File.ReadAllText(pawnBts[pi]);
                            string role = btText.Contains("launch_hook") || btText.Contains("punch_") ? "Grappler"
                                        : btText.Contains("shoot_rifle") || btText.Contains("shoot_pistol") ? "Gunner"
                                        : "Unknown";
                            string label = $"Pawn {pi} ({role})";
                            // Wrap pawn roots in a Selector with comment label
                            combined.Add(new Dictionary<string, object?>
                            {
                                ["type"] = "Selector",
                                ["comment"] = label,
                                ["children"] = pawnNodes
                            });
                        }
                        await ServeJson(res, combined);
                    }
                    else
                    { res.StatusCode = 404; await WriteText(res, "BT not found"); }
                }
                return;
            }

            if (parts.Length >= subIdx + 1 && parts[subIdx] == "battles")
            {
                var battlesDir = Path.Combine(fighterDir, "battles");
                if (!Directory.Exists(battlesDir)) { res.StatusCode = 404; await WriteText(res, "No battles"); return; }

                int battleIdx = subIdx + 1;

                if (parts.Length == subIdx + 1)
                {
                    // List battles — include filename for each so the client can reference the path
                    // Supports both BattleLog (fight) and BeaconBattleLog (beacon brawl) formats
                    var battles = new List<object>();
                    foreach (var file in Directory.GetFiles(battlesDir, "*.json").OrderBy(f => f))
                    {
                        var json = File.ReadAllText(file);
                        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json, TournamentJson.Options);
                        if (dict != null)
                        {
                            dict["_filename"] = Path.GetFileName(file);
                            // Normalize: beacon brawl uses "team"/"opponent" but dashboard expects "fighter"/"opponent"
                            if (dict.ContainsKey("team") && !dict.ContainsKey("fighter"))
                                dict["fighter"] = dict["team"];
                            battles.Add(dict);
                        }
                    }
                    await ServeJson(res, battles);
                    return;
                }

                if (parts.Length == battleIdx + 1)
                {
                    // Single battle log
                    string battleFile = parts[battleIdx];
                    if (!battleFile.EndsWith(".json")) battleFile += ".json";
                    var battlePath = Path.Combine(battlesDir, battleFile);
                    if (File.Exists(battlePath))
                        await ServeRawJson(res, File.ReadAllText(battlePath));
                    else
                    { res.StatusCode = 404; await WriteText(res, "Battle not found"); }
                    return;
                }
            }
        }

        res.StatusCode = 404;
        await WriteText(res, "Not found");
    }

    // ── SubTree API ──

    private async System.Threading.Tasks.Task ServeSubTreeData(HttpListenerResponse res, string[] parts)
    {
        // /api/generations/sub-trees → pseudo generation summary
        if (parts.Length == 3)
        {
            await ServeJson(res, new { generation = "sub-trees", leaderboard = SubTrees.All.Keys.Select(
                (name, i) => new { rank = i + 1, name, fighter_id = name, color = "#1ae5e5" }).ToArray() });
            return;
        }

        // /api/generations/sub-trees/fighters → list all subtrees
        if (parts.Length >= 4 && parts[3] == "fighters")
        {
            if (parts.Length == 4)
            {
                var fighters = SubTrees.All.Keys.Select(
                    (name, i) => new { name, fighter_id = name, color = "#1ae5e5" }).ToArray();
                await ServeJson(res, fighters);
                return;
            }

            // /api/generations/sub-trees/fighters/{name}/bt
            string stName = parts[4];
            if (parts.Length >= 6 && parts[5] == "bt")
            {
                if (SubTrees.All.TryGetValue(stName, out var builder))
                {
                    var bt = builder();
                    var json = BtSerializer.Serialize(bt);
                    await ServeRawJson(res, json);
                }
                else
                { res.StatusCode = 404; await WriteText(res, "SubTree not found"); }
                return;
            }
        }

        res.StatusCode = 404;
        await WriteText(res, "Not found");
    }

    // ── Dashboard HTML ──

    private async System.Threading.Tasks.Task ServeDashboard(HttpListenerResponse res)
    {
        res.ContentType = "text/html; charset=utf-8";
        await WriteBytes(res, Encoding.UTF8.GetBytes(DashboardHtml.Build()));
    }

    // ── Test Runner ──

    private async System.Threading.Tasks.Task RunTests(HttpListenerResponse res)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _lastTestResults = MovementTests.RunAll();
        _lastMapTestResults = MapTests.RunAll();
        _lastBbMapTestResults = BeaconMapTests.RunAll();
        _lastMatchupResults = BeaconMapTests.RunInformedVsUninformed(gamesPerMatchup: 10);
        stopwatch.Stop();
        _testsRanAt = DateTime.UtcNow;

        int passed = _lastTestResults.Count(r => r.Passed);
        int failed = _lastTestResults.Count(r => !r.Passed);
        int mapPassed = _lastMapTestResults.Count(r => r.Passed);
        int mapFailed = _lastMapTestResults.Count(r => !r.Passed);
        int bbPassed = _lastBbMapTestResults.Count(r => r.Passed);
        int bbFailed = _lastBbMapTestResults.Count(r => !r.Passed);
        GD.Print($"Tests: {passed}p/{failed}f; Map: {mapPassed}p/{mapFailed}f; BB: {bbPassed}p/{bbFailed}f in {stopwatch.ElapsedMilliseconds}ms");

        // Save battle logs to disk for replay launching
        SaveMapTestBattleLogs();
        SaveBbMapTestBattleLogs();

        // Slim map results for the response (no full BattleLog — it's huge)
        var slimMapResults = _lastMapTestResults.Select(r => new
        {
            map_name = r.MapName, passed = r.Passed, error = r.Error,
            duration_ticks = r.DurationTicks,
            fighter0_hp = r.Fighter0Hp, fighter1_hp = r.Fighter1Hp,
            winner_index = r.WinnerIndex, feature_notes = r.FeatureNotes,
            has_replay = r.BattleLog?.Replay != null
        });

        var slimBbResults = _lastBbMapTestResults.Select(r => new
        {
            map_name = r.MapName, passed = r.Passed, error = r.Error,
            duration_ticks = r.DurationTicks,
            final_scores = r.FinalScores, kills = r.Kills,
            winner_team = r.WinnerTeam, feature_notes = r.FeatureNotes,
            has_replay = r.BattleLog?.Replay != null
        });

        await ServeJson(res, new
        {
            elapsed_ms = stopwatch.ElapsedMilliseconds,
            passed, failed, results = _lastTestResults,
            map_passed = mapPassed, map_failed = mapFailed, map_results = slimMapResults,
            bb_passed = bbPassed, bb_failed = bbFailed, bb_results = slimBbResults,
            matchup_results = _lastMatchupResults?.Select(r => new
            {
                label = r.Label, map = r.MapName,
                informed_wins = r.InformedWins, uninformed_wins = r.UninformedWins,
                draws = r.Draws, total = r.TotalGames,
                informed_win_rate = r.InformedWinRate,
                informed_avg_score = r.InformedAvgScore,
                uninformed_avg_score = r.UninformedAvgScore
            })
        });
    }

    private void SaveMapTestBattleLogs()
    {
        if (_lastMapTestResults == null) return;
        Directory.CreateDirectory(_mapTestDir);

        // Clean old files
        foreach (var f in Directory.GetFiles(_mapTestDir, "*.json"))
            File.Delete(f);

        foreach (var r in _lastMapTestResults)
        {
            if (r.BattleLog == null) continue;
            var path = Path.Combine(_mapTestDir, $"{r.MapName}.json");
            var json = JsonSerializer.Serialize(r.BattleLog, TournamentJson.Options);
            File.WriteAllText(path, json);
        }
    }

    private async System.Threading.Tasks.Task ServeMapTestResults(HttpListenerResponse res)
    {
        if (_lastMapTestResults == null)
        {
            await ServeJson(res, Array.Empty<object>());
            return;
        }
        // Serve without the full BattleLog to keep the response small
        var slim = _lastMapTestResults.Select(r => new
        {
            map_name = r.MapName,
            passed = r.Passed,
            error = r.Error,
            duration_ticks = r.DurationTicks,
            fighter0_hp = r.Fighter0Hp,
            fighter1_hp = r.Fighter1Hp,
            winner_index = r.WinnerIndex,
            feature_notes = r.FeatureNotes,
            has_replay = r.BattleLog?.Replay != null
        });
        await ServeJson(res, slim);
    }

    private async System.Threading.Tasks.Task LaunchMapTestReplay(HttpListenerRequest req, HttpListenerResponse res)
    {
        string body;
        using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
            body = await reader.ReadToEndAsync();

        var payload = JsonSerializer.Deserialize<JsonElement>(body);
        var mapName = payload.TryGetProperty("map", out var m) ? m.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(mapName))
        {
            res.StatusCode = 400;
            await ServeJson(res, new { error = "Missing map name" });
            return;
        }

        var battleLogPath = Path.Combine(_mapTestDir, $"{mapName}.json");
        if (!File.Exists(battleLogPath))
        {
            res.StatusCode = 404;
            await ServeJson(res, new { error = $"No battle log for map '{mapName}'. Run tests first." });
            return;
        }

        // Write replay config
        var configPath = Path.Combine(_projectDir, "replay_config.json");
        var configJson = JsonSerializer.Serialize(new { battle_log_path = battleLogPath }, TournamentJson.Options);
        File.WriteAllText(configPath, configJson);

        // Launch Godot with replay scene
        var godotPath = @"C:\Program Files\godot\godot.exe";
        var args = $"--path \"{_projectDir}\" --scene res://scenes/replay.tscn";
        GD.Print($"Launching map test replay ({mapName}): {godotPath} {args}");

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(godotPath, args)
            {
                UseShellExecute = false,
                WorkingDirectory = _projectDir
            };
            System.Diagnostics.Process.Start(psi);
            await ServeJson(res, new { status = "launched", map = mapName, battle_log = battleLogPath });
        }
        catch (Exception ex)
        {
            res.StatusCode = 500;
            await ServeJson(res, new { error = $"Failed to launch Godot: {ex.Message}" });
        }
    }

    private void SaveBbMapTestBattleLogs()
    {
        if (_lastBbMapTestResults == null) return;
        Directory.CreateDirectory(_bbMapTestDir);
        foreach (var f in Directory.GetFiles(_bbMapTestDir, "*.json")) File.Delete(f);
        foreach (var r in _lastBbMapTestResults)
        {
            if (r.BattleLog == null) continue;
            var path = Path.Combine(_bbMapTestDir, $"{r.MapName}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(r.BattleLog, TournamentJson.Options));
        }
    }

    private async System.Threading.Tasks.Task ServeBbMapTestResults(HttpListenerResponse res)
    {
        if (_lastBbMapTestResults == null) { await ServeJson(res, Array.Empty<object>()); return; }
        var slim = _lastBbMapTestResults.Select(r => new
        {
            map_name = r.MapName, passed = r.Passed, error = r.Error,
            duration_ticks = r.DurationTicks, final_scores = r.FinalScores,
            kills = r.Kills, winner_team = r.WinnerTeam,
            feature_notes = r.FeatureNotes, has_replay = r.BattleLog?.Replay != null
        });
        await ServeJson(res, slim);
    }

    private async System.Threading.Tasks.Task LaunchBbMapTestReplay(HttpListenerRequest req, HttpListenerResponse res)
    {
        string body;
        using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
            body = await reader.ReadToEndAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(body);
        var mapName = payload.TryGetProperty("map", out var m) ? m.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(mapName)) { res.StatusCode = 400; await ServeJson(res, new { error = "Missing map name" }); return; }

        var battleLogPath = Path.Combine(_bbMapTestDir, $"{mapName}.json");
        if (!File.Exists(battleLogPath)) { res.StatusCode = 404; await ServeJson(res, new { error = $"No battle log for '{mapName}'. Run tests first." }); return; }

        var configPath = Path.Combine(_projectDir, "replay_config.json");
        File.WriteAllText(configPath, JsonSerializer.Serialize(new { battle_log_path = battleLogPath }, TournamentJson.Options));

        var godotPath = @"C:\Program Files\godot\godot.exe";
        var args = $"--path \"{_projectDir}\" --scene res://scenes/replay.tscn";
        GD.Print($"Launching BB map test replay ({mapName}): {godotPath} {args}");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(godotPath, args) { UseShellExecute = false, WorkingDirectory = _projectDir });
            await ServeJson(res, new { status = "launched", map = mapName });
        }
        catch (Exception ex) { res.StatusCode = 500; await ServeJson(res, new { error = $"Failed to launch: {ex.Message}" }); }
    }

    // ── Screenshots ──

    private async System.Threading.Tasks.Task ServeScreenshotsPage(HttpListenerResponse res)
    {
        res.ContentType = "text/html; charset=utf-8";
        await WriteBytes(res, Encoding.UTF8.GetBytes(ScreenshotsHtml.Build()));
    }

    private async System.Threading.Tasks.Task ServeScreenshotsList(HttpListenerResponse res)
    {
        var ssDir = Path.Combine(_projectDir, "debug", "screenshots");
        var items = new List<object>();
        if (Directory.Exists(ssDir))
        {
            foreach (var jsonFile in Directory.GetFiles(ssDir, "*.json").OrderByDescending(f => f))
            {
                var name = Path.GetFileNameWithoutExtension(jsonFile);
                var pngFile = Path.Combine(ssDir, name + ".png");
                bool hasPng = File.Exists(pngFile);
                string stateJson = File.ReadAllText(jsonFile);
                items.Add(new { name, has_image = hasPng, state = JsonSerializer.Deserialize<JsonElement>(stateJson) });
            }
        }
        await ServeJson(res, items);
    }

    private async System.Threading.Tasks.Task ServeScreenshotImage(HttpListenerResponse res, string path)
    {
        // path: /api/screenshots/img/{name}.png
        var parts = path.Split('/');
        var filename = parts[^1];
        var filePath = Path.Combine(_projectDir, "debug", "screenshots", filename);
        if (!File.Exists(filePath)) { res.StatusCode = 404; await WriteText(res, "Not found"); return; }
        res.ContentType = "image/png";
        var bytes = File.ReadAllBytes(filePath);
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    private async System.Threading.Tasks.Task ClearScreenshots(HttpListenerResponse res)
    {
        var ssDir = Path.Combine(_projectDir, "debug", "screenshots");
        int count = 0;
        if (Directory.Exists(ssDir))
        {
            foreach (var f in Directory.GetFiles(ssDir)) { File.Delete(f); count++; }
        }
        GD.Print($"Cleared {count} screenshot files");
        await ServeJson(res, new { cleared = count });
    }

    private async System.Threading.Tasks.Task ServeTestPage(HttpListenerResponse res)
    {
        res.ContentType = "text/html; charset=utf-8";
        await WriteBytes(res, Encoding.UTF8.GetBytes(TestPageHtml.Build()));
    }

    // ── Replay Launch (opens Godot window) ──

    private async System.Threading.Tasks.Task LaunchReplay(HttpListenerRequest req, HttpListenerResponse res)
    {
        // Read request body
        string body;
        using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
            body = await reader.ReadToEndAsync();

        var payload = JsonSerializer.Deserialize<JsonElement>(body);
        var tournament = payload.TryGetProperty("tournament", out var t) ? t.GetString() ?? "default" : "default";
        var gen = payload.TryGetProperty("gen", out var g) ? g.GetString() ?? "" : "";
        var fighter = payload.TryGetProperty("fighter", out var f) ? f.GetString() ?? "" : "";
        var matchFile = payload.TryGetProperty("match", out var m) ? m.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(gen) || string.IsNullOrEmpty(fighter) || string.IsNullOrEmpty(matchFile))
        {
            res.StatusCode = 400;
            await ServeJson(res, new { error = "Missing gen, fighter, or match" });
            return;
        }

        // Resolve battle log path — support both "fighters" and "teams" directories
        var outPath = GetTournamentOutputPath(tournament);
        var genDir = Path.Combine(outPath, gen);
        if (!Directory.Exists(genDir))
        {
            // Try zero-padded format (gen_000)
            if (int.TryParse(gen.Replace("gen_", ""), out int genNum))
                genDir = Path.Combine(outPath, $"gen_{genNum:D3}");
        }

        // Find the fighters/teams directory
        var entitiesDir = Path.Combine(genDir, "fighters");
        if (!Directory.Exists(entitiesDir))
            entitiesDir = Path.Combine(genDir, "teams");

        // Find the fighter/team directory by prefix match (handles fighter_02 → team_02_Name)
        string? entityDir = null;
        if (Directory.Exists(entitiesDir))
        {
            // Try exact match first, then prefix match on the ID portion
            entityDir = Directory.GetDirectories(entitiesDir)
                .FirstOrDefault(d => Path.GetFileName(d) == fighter);
            if (entityDir == null)
            {
                // Extract the numeric ID and match across fighter/team naming (fighter_02 → team_02)
                var numericPart = System.Text.RegularExpressions.Regex.Match(fighter, @"\d+");
                if (numericPart.Success)
                {
                    entityDir = Directory.GetDirectories(entitiesDir)
                        .FirstOrDefault(d => System.Text.RegularExpressions.Regex.IsMatch(
                            Path.GetFileName(d), $@"^(fighter|team)_{numericPart.Value}(_|$)"));
                }
            }
            // Fallback: try StartsWith on the directory name
            entityDir ??= Directory.GetDirectories(entitiesDir)
                .FirstOrDefault(d => Path.GetFileName(d).StartsWith(fighter));
        }

        if (entityDir == null)
        {
            res.StatusCode = 404;
            await ServeJson(res, new { error = $"Fighter/team not found: {fighter} in {entitiesDir}" });
            return;
        }

        if (!matchFile.EndsWith(".json")) matchFile += ".json";
        var battleLogPath = Path.Combine(entityDir, "battles", matchFile);

        if (!File.Exists(battleLogPath))
        {
            res.StatusCode = 404;
            await ServeJson(res, new { error = $"Battle log not found: {battleLogPath}" });
            return;
        }

        // Write replay config
        var configPath = Path.Combine(_projectDir, "replay_config.json");
        var configJson = JsonSerializer.Serialize(new { battle_log_path = battleLogPath }, TournamentJson.Options);
        File.WriteAllText(configPath, configJson);

        // Launch Godot with replay scene (visible window)
        var godotPath = @"C:\Program Files\godot\godot.exe";
        var args = $"--path \"{_projectDir}\" --scene res://scenes/replay.tscn";
        GD.Print($"Launching replay: {godotPath} {args}");

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(godotPath, args)
            {
                UseShellExecute = false,
                WorkingDirectory = _projectDir
            };
            System.Diagnostics.Process.Start(psi);
            await ServeJson(res, new { status = "launched", battle_log = battleLogPath });
        }
        catch (Exception ex)
        {
            res.StatusCode = 500;
            await ServeJson(res, new { error = $"Failed to launch Godot: {ex.Message}" });
        }
    }

    // ── BT Compare ──

    private async System.Threading.Tasks.Task ServeBtCompare(HttpListenerResponse res)
    {
        res.ContentType = "text/html; charset=utf-8";
        await WriteBytes(res, Encoding.UTF8.GetBytes(BtCompareHtml.Build()));
    }

    // ── Replay Viewer ──

    private async System.Threading.Tasks.Task ServeReplayViewer(HttpListenerResponse res)
    {
        res.ContentType = "text/html; charset=utf-8";
        await WriteBytes(res, Encoding.UTF8.GetBytes(ReplayViewerHtml.Build()));
    }

    // ── Helpers ──

    private static async System.Threading.Tasks.Task ServeJson(HttpListenerResponse res, object data)
    {
        res.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(data, TournamentJson.Options);
        await WriteBytes(res, Encoding.UTF8.GetBytes(json));
    }

    private static async System.Threading.Tasks.Task ServeRawJson(HttpListenerResponse res, string json)
    {
        res.ContentType = "application/json; charset=utf-8";
        await WriteBytes(res, Encoding.UTF8.GetBytes(json));
    }

    private static async System.Threading.Tasks.Task WriteText(HttpListenerResponse res, string text)
    {
        res.ContentType = "text/plain; charset=utf-8";
        await WriteBytes(res, Encoding.UTF8.GetBytes(text));
    }

    private static async System.Threading.Tasks.Task WriteBytes(HttpListenerResponse res, byte[] bytes)
    {
        try
        {
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes);
        }
        finally
        {
            res.Close();
        }
    }
}
