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
    private string _mapTestDir = "";

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
            else if (path == "/api/replay/launch" && method == "POST")
                await LaunchReplay(req, res);
            else if (path == "/api/tests/run" && method == "POST")
                await RunTests(res);
            else if (path == "/api/tests/results" && method == "GET")
                await ServeJson(res, _lastTestResults ?? new List<TestResult>());
            else if (path == "/api/tests/map-results" && method == "GET")
                await ServeMapTestResults(res);
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
        await WriteBytes(res, Encoding.UTF8.GetBytes(BuildDashboardHtml()));
    }

    // ── Test Runner ──

    private async System.Threading.Tasks.Task RunTests(HttpListenerResponse res)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _lastTestResults = MovementTests.RunAll();
        _lastMapTestResults = MapTests.RunAll();
        stopwatch.Stop();

        int passed = _lastTestResults.Count(r => r.Passed);
        int failed = _lastTestResults.Count(r => !r.Passed);
        int mapPassed = _lastMapTestResults.Count(r => r.Passed);
        int mapFailed = _lastMapTestResults.Count(r => !r.Passed);
        GD.Print($"Tests: {passed} passed, {failed} failed; Map: {mapPassed} passed, {mapFailed} failed in {stopwatch.ElapsedMilliseconds}ms");

        // Save map test battle logs to disk for replay launching
        SaveMapTestBattleLogs();

        // Slim map results for the response (no full BattleLog — it's huge)
        var slimMapResults = _lastMapTestResults.Select(r => new
        {
            map_name = r.MapName, passed = r.Passed, error = r.Error,
            duration_ticks = r.DurationTicks,
            fighter0_hp = r.Fighter0Hp, fighter1_hp = r.Fighter1Hp,
            winner_index = r.WinnerIndex, feature_notes = r.FeatureNotes,
            has_replay = r.BattleLog?.Replay != null
        });

        await ServeJson(res, new
        {
            elapsed_ms = stopwatch.ElapsedMilliseconds,
            passed, failed, results = _lastTestResults,
            map_passed = mapPassed, map_failed = mapFailed, map_results = slimMapResults
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

    private async System.Threading.Tasks.Task ServeTestPage(HttpListenerResponse res)
    {
        res.ContentType = "text/html; charset=utf-8";
        await WriteBytes(res, Encoding.UTF8.GetBytes(BuildTestPageHtml()));
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
        await WriteBytes(res, Encoding.UTF8.GetBytes(BuildBtCompareHtml()));
    }

    // ── Replay Viewer ──

    private async System.Threading.Tasks.Task ServeReplayViewer(HttpListenerResponse res)
    {
        res.ContentType = "text/html; charset=utf-8";
        await WriteBytes(res, Encoding.UTF8.GetBytes(BuildReplayViewerHtml()));
    }

    private static string BuildDashboardHtml() => """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>AI-BT-Gym Tournament</title>
<style>
  :root { --bg: #0d1117; --card: #161b22; --border: #30363d; --text: #e6edf3; --dim: #8b949e; --accent: #58a6ff; --green: #3fb950; --red: #f85149; --yellow: #d29922; }
  * { margin: 0; padding: 0; box-sizing: border-box; }
  body { font-family: 'Segoe UI', system-ui, sans-serif; background: var(--bg); color: var(--text); padding: 20px; }
  h1 { color: var(--accent); margin-bottom: 4px; }
  .subtitle { color: var(--dim); margin-bottom: 20px; }
  .controls { display: flex; gap: 12px; align-items: center; margin-bottom: 24px; }
  button { background: var(--accent); color: #fff; border: none; padding: 10px 20px; border-radius: 6px; cursor: pointer; font-size: 14px; font-weight: 600; }
  button:hover { opacity: 0.9; }
  button:disabled { opacity: 0.5; cursor: not-allowed; }
  .status { color: var(--dim); font-size: 14px; }
  .card { background: var(--card); border: 1px solid var(--border); border-radius: 8px; padding: 16px; margin-bottom: 16px; }
  .card h2 { color: var(--accent); font-size: 16px; margin-bottom: 12px; }
  table { width: 100%; border-collapse: collapse; font-size: 14px; }
  th { text-align: left; color: var(--dim); font-weight: 500; padding: 8px; border-bottom: 1px solid var(--border); }
  td { padding: 8px; border-bottom: 1px solid var(--border); }
  tr:hover td { background: rgba(88,166,255,0.05); }
  .win { color: var(--green); } .loss { color: var(--red); } .draw { color: var(--yellow); }
  .elo { font-weight: 600; font-variant-numeric: tabular-nums; }
  .record { font-variant-numeric: tabular-nums; }
  .clickable { cursor: pointer; text-decoration: underline; text-decoration-color: var(--border); }
  .clickable:hover { text-decoration-color: var(--accent); }
  .metric { display: inline-block; background: var(--bg); padding: 4px 10px; border-radius: 4px; margin: 2px 4px 2px 0; font-size: 13px; }
  .metric span { color: var(--dim); }
  .bar { height: 6px; border-radius: 3px; background: var(--border); overflow: hidden; }
  .bar-fill { height: 100%; border-radius: 3px; }
  .phase-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; }
  .phase-card { background: var(--bg); padding: 12px; border-radius: 6px; }
  .phase-card h4 { color: var(--dim); text-transform: uppercase; font-size: 12px; margin-bottom: 8px; }
  .hit-timeline { max-height: 300px; overflow-y: auto; font-size: 13px; }
  .hit-row { padding: 4px 0; border-bottom: 1px solid var(--border); display: flex; justify-content: space-between; }
  .moment { padding: 6px 0; border-bottom: 1px solid var(--border); }
  .moment .event-type { color: var(--accent); font-weight: 600; font-size: 12px; text-transform: uppercase; }
  details.rounds-foldout { margin-bottom: 16px; }
  details.rounds-foldout > summary { cursor: pointer; padding: 10px 16px; background: var(--card); border: 1px solid var(--border); border-radius: 8px; font-size: 14px; font-weight: 600; color: var(--accent); list-style: none; display: flex; align-items: center; gap: 8px; }
  details.rounds-foldout > summary::-webkit-details-marker { display: none; }
  details.rounds-foldout > summary::before { content: '▸'; transition: transform 0.15s; display: inline-block; }
  details.rounds-foldout[open] > summary::before { transform: rotate(90deg); }
  details.rounds-foldout[open] > summary { border-radius: 8px 8px 0 0; border-bottom: none; }
  details.rounds-foldout > .rounds-body { border: 1px solid var(--border); border-top: none; border-radius: 0 0 8px 8px; background: var(--card); }
  details.rounds-foldout .round-row { padding: 8px 16px; border-bottom: 1px solid var(--border); display: flex; justify-content: space-between; align-items: center; cursor: pointer; font-size: 14px; }
  details.rounds-foldout .round-row:last-child { border-bottom: none; }
  details.rounds-foldout .round-row:hover { background: rgba(88,166,255,0.05); }
  .breadcrumb { font-size: 14px; margin-bottom: 16px; color: var(--dim); display: flex; align-items: center; flex-wrap: wrap; }
  .breadcrumb a { color: var(--accent); text-decoration: none; cursor: pointer; }
  .breadcrumb a:hover { text-decoration: underline; }
  .breadcrumb .sep { margin: 0 6px; color: var(--border); }
  .breadcrumb .current { color: var(--text); font-weight: 600; }
  .copy-path { margin-left: 10px; background: var(--card); border: 1px solid var(--border); color: var(--dim); padding: 2px 8px; border-radius: 4px; cursor: pointer; font-size: 12px; font-family: monospace; white-space: nowrap; }
  .copy-path:hover { color: var(--accent); border-color: var(--accent); }
</style>
</head>
<body>
<div style="display:flex;justify-content:space-between;align-items:center">
  <div style="display:flex;align-items:center;gap:16px">
    <div><h1>AI-BT-Gym</h1><p class="subtitle">Evolutionary Behavior Tree Tournament</p></div>
    <select id="tournamentSelect" onchange="switchTournament(this.value)" style="background:var(--card);color:var(--text);border:1px solid var(--border);border-radius:6px;padding:8px 12px;font-size:14px;cursor:pointer;min-width:160px">
      <option value="default">Loading...</option>
    </select>
  </div>
  <div style="display:flex;gap:8px">
    <a id="btCompareLink" href="/bt-compare" style="background:var(--card);border:1px solid var(--border);color:var(--accent);padding:8px 16px;border-radius:6px;text-decoration:none;font-weight:600;font-size:14px">BT Compare</a>
    <a href="/tests" style="background:var(--card);border:1px solid var(--border);color:var(--accent);padding:8px 16px;border-radius:6px;text-decoration:none;font-weight:600;font-size:14px">Tests</a>
  </div>
</div>
<div id="breadcrumb" class="breadcrumb"></div>
<div id="content"></div>

<script>
let currentTournament = 'default';
let tournaments = [];
let genCache = {}; // gen number -> {timestamp, ...}

// Navigation state
let nav = { view: 'home' };

function apiBase() {
  return currentTournament === 'default' ? '/api' : `/api/tournaments/${currentTournament}`;
}

async function api(path) {
  const r = await fetch(apiBase() + path);
  return r.json();
}

async function loadTournaments() {
  const r = await fetch('/api/tournaments');
  tournaments = await r.json();
  const sel = document.getElementById('tournamentSelect');
  sel.innerHTML = '';
  tournaments.forEach(t => {
    const opt = document.createElement('option');
    opt.value = t.id;
    opt.textContent = t.display_name + (t.generation_count > 0 ? ` (${t.generation_count} gens)` : '');
    sel.appendChild(opt);
  });
  // Restore from URL hash or default
  const params = new URLSearchParams(location.search);
  if (params.has('tournament')) {
    currentTournament = params.get('tournament');
  }
  sel.value = currentTournament;
  updateBtCompareLink();
}

function switchTournament(tid) {
  currentTournament = tid;
  genCache = {};
  updateBtCompareLink();
  // Update URL
  const url = new URL(location.href);
  if (tid === 'default') url.searchParams.delete('tournament');
  else url.searchParams.set('tournament', tid);
  history.replaceState(null, '', url);
  showHome();
}

function updateBtCompareLink() {
  const link = document.getElementById('btCompareLink');
  if (link) link.href = currentTournament === 'default' ? '/bt-compare' : `/bt-compare?tournament=${currentTournament}`;
}

function fmtTimestamp(ts) {
  if (!ts) return '';
  const d = new Date(ts);
  const pad = n => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth()+1)}-${pad(d.getDate())}_${pad(d.getHours())}-${pad(d.getMinutes())}`;
}

function getRelativePath() {
  const pad = n => String(n).padStart(3, '0');
  const base = currentTournament === 'default' ? 'generations' : `tournaments/${currentTournament}`;
  if (nav.view === 'home') return base;
  if (nav.view === 'gen') return `${base}/gen_${pad(nav.gen)}`;
  if (nav.view === 'fighter') return `${base}/gen_${pad(nav.gen)}/fighters/${nav.fighterId}_${nav.fighterName}`;
  if (nav.view === 'battle') return `${base}/gen_${pad(nav.gen)}/fighters/${nav.fighterId}_${nav.fighterName}/battles/${nav.matchFile || ''}`;
  return '';
}

function copyPath(ev) {
  const p = getRelativePath();
  if (!p) return;
  navigator.clipboard.writeText(p);
  const btn = ev.target;
  const orig = btn.textContent;
  btn.textContent = 'copied!';
  setTimeout(() => btn.textContent = orig, 1200);
}

function renderBreadcrumb() {
  const el = document.getElementById('breadcrumb');
  const crumbs = [{label: 'Tournaments', action: 'showHome()'}];

  if (nav.view !== 'home') {
    const genLabel = `Gen_${nav.gen}` + (nav.genTimestamp ? `_${fmtTimestamp(nav.genTimestamp)}` : '');
    crumbs.push({label: genLabel, action: `showGeneration(${nav.gen})`});
  }
  if (nav.view === 'fighter' || nav.view === 'battle') {
    crumbs.push({label: nav.fighterName || nav.fighterId, action: `showFighter(${nav.gen}, '${nav.fighterId}')`});
  }
  if (nav.view === 'battle') {
    crumbs.push({label: `vs ${nav.opponentName}`, action: null});
  }

  const path = getRelativePath();
  const copyBtn = path ? `<button class="copy-path" onclick="copyPath(event)" title="${path}">${path}</button>` : '';

  el.innerHTML = crumbs.map((c, i) => {
    const isLast = i === crumbs.length - 1;
    const sep = i > 0 ? '<span class="sep">&gt;</span>' : '';
    if (isLast) return `${sep}<span class="current">${c.label}</span>`;
    return `${sep}<a onclick="${c.action}">${c.label}</a>`;
  }).join('') + copyBtn;
}

async function showHome(fromPop) {
  nav = { view: 'home' };
  if (!fromPop) pushNav();
  renderBreadcrumb();

  const gens = await api('/generations');
  const el = document.getElementById('content');

  const controls = `<div class="controls">
    <button id="runBtn" onclick="runTournament()">Run Tournament</button>
    <span id="statusText" class="status"></span>
  </div>`;

  if (gens.length === 0) {
    el.innerHTML = controls + '<div class="card"><p style="color:var(--dim)">No generations yet. Click "Run Tournament" to start.</p></div>';
    return;
  }

  // Cache timestamps
  gens.forEach(g => { genCache[g.generation] = g.timestamp; });

  el.innerHTML = controls + gens.map(g => `
    <div class="card clickable" onclick="showGeneration(${g.generation})">
      <h2>Gen_${g.generation}_${fmtTimestamp(g.timestamp)}</h2>
      <div style="margin-bottom:8px">
        <span class="metric"><span>Fighters:</span> ${g.fighter_count}</span>
        <span class="metric"><span>Matches:</span> ${g.total_matches}</span>
        <span class="metric"><span>KO Rate:</span> ${(g.meta_stats.knockout_rate*100).toFixed(0)}%</span>
        <span class="metric"><span>Avg Duration:</span> ${(g.meta_stats.avg_match_duration_ticks/60).toFixed(1)}s</span>
      </div>
      <table>
        <tr><th>#</th><th>Fighter</th><th>ELO</th><th>Record</th></tr>
        ${g.leaderboard.map(e => `
          <tr onclick="showFighter(${g.generation}, '${e.fighter_id}', '${e.name}')" style="cursor:pointer">
            <td>${e.rank}</td>
            <td>${e.color ? `<span style="display:inline-block;width:10px;height:10px;border-radius:50%;background:${e.color};margin-right:6px;vertical-align:middle"></span>` : ''}${e.name}</td>
            <td class="elo">${e.elo.toFixed(1)}</td>
            <td class="record">${e.record}</td>
          </tr>
        `).join('')}
      </table>
    </div>
  `).join('');
}

async function runTournament() {
  const btn = document.getElementById('runBtn');
  const status = document.getElementById('statusText');
  btn.disabled = true;
  status.textContent = 'Running tournament...';
  try {
    const url = currentTournament === 'default'
      ? '/api/tournament/run'
      : `/api/tournaments/${currentTournament}/run`;
    const r = await fetch(url, { method: 'POST' });
    const data = await r.json();
    if (data.error) { status.textContent = 'Error: ' + data.error; }
    else { status.textContent = `Gen ${data.generation} completed in ${data.elapsed_ms}ms`; }
    showHome();
  } catch(e) { status.textContent = 'Failed: ' + e.message; }
  btn.disabled = false;
}

async function showGeneration(gen, fromPop) {
  const genData = await api(`/generations/${gen}`);
  nav = { view: 'gen', gen, genTimestamp: genData.timestamp || genCache[gen] };
  genCache[gen] = nav.genTimestamp;
  if (!fromPop) pushNav();
  renderBreadcrumb();

  const fighters = await api(`/generations/${gen}/fighters`);
  const el = document.getElementById('content');
  el.innerHTML = fighters.map(f => `
    <div class="card clickable" onclick="showFighter(${gen}, '${f.fighter_id}', '${f.name}')">
      <h2>${f.color ? `<span style="display:inline-block;width:12px;height:12px;border-radius:50%;background:${f.color};margin-right:8px;vertical-align:middle"></span>` : ''}${f.name}</h2>
      <div>
        <span class="metric win"><span>W:</span> ${f.record.wins}</span>
        <span class="metric loss"><span>L:</span> ${f.record.losses}</span>
        <span class="metric draw"><span>D:</span> ${f.record.draws}</span>
        <span class="metric"><span>ELO:</span> <strong class="elo">${f.elo.toFixed(1)}</strong></span>
        <span class="metric"><span>Win Rate:</span> ${(f.win_rate*100).toFixed(0)}%</span>
      </div>
      <div style="margin-top:6px">
        ${f.aggregate_metrics.avg_damage_dealt != null ? `
          <span class="metric"><span>Avg Dmg Dealt:</span> ${f.aggregate_metrics.avg_damage_dealt.toFixed(1)}</span>
          <span class="metric"><span>Avg Dmg Taken:</span> ${f.aggregate_metrics.avg_damage_received.toFixed(1)}</span>
          <span class="metric"><span>Hit Accuracy:</span> ${(f.aggregate_metrics.avg_hit_accuracy*100).toFixed(0)}%</span>
          <span class="metric"><span>KOs:</span> ${f.aggregate_metrics.total_knockouts}</span>
        ` : `
          <span class="metric"><span>Avg Score Diff:</span> ${(f.aggregate_metrics.avg_score_diff || 0).toFixed(1)}</span>
          <span class="metric"><span>Beacon Own%:</span> ${((f.aggregate_metrics.avg_beacon_ownership_pct || 0)*100).toFixed(0)}%</span>
          <span class="metric"><span>Avg Pulse:</span> ${(f.aggregate_metrics.avg_pulse_count || 0).toFixed(1)}</span>
          <span class="metric"><span>Score Wins:</span> ${f.aggregate_metrics.total_score_victories || 0}</span>
        `}
      </div>
    </div>
  `).join('');
}

async function showFighter(gen, fighterId, fighterName, fromPop) {
  if (!fighterName) {
    const fighters = await api(`/generations/${gen}/fighters`);
    const f = fighters.find(f => f.fighter_id === fighterId);
    fighterName = f ? f.name : fighterId;
  }
  nav = { ...nav, view: 'fighter', gen, fighterId, fighterName };
  if (!nav.genTimestamp) nav.genTimestamp = genCache[gen];
  if (!fromPop) pushNav();
  renderBreadcrumb();

  const battles = await api(`/generations/${gen}/fighters/${fighterId}/battles`);
  // Store battles for detail drill-down
  window._cachedBattles = battles;

  const wins = battles.filter(b => b.result === 'win').length;
  const losses = battles.filter(b => b.result === 'loss').length;
  const draws = battles.filter(b => b.result === 'draw').length;

  const el = document.getElementById('content');
  el.innerHTML = `
    <details class="rounds-foldout" open>
      <summary>Round Pairings (${battles.length} matches — <span class="win">${wins}W</span> <span class="loss">${losses}L</span> <span class="draw">${draws}D</span>)</summary>
      <div class="rounds-body">
        ${battles.map((b, i) => `
          <div class="round-row" onclick='showBattleByIndex(${i}, "${fighterId}")'>
            <div>
              <span style="color:var(--dim);min-width:24px;display:inline-block">${i+1}.</span>
              vs <strong>${b.opponent}</strong>
              ${b.final_state ? `
                <span style="color:var(--dim);margin-left:12px">${b.duration_seconds.toFixed(1)}s — HP ${b.final_state.fighter_hp.toFixed(0)} vs ${b.final_state.opponent_hp.toFixed(0)}</span>
              ` : `
                <span style="color:var(--dim);margin-left:12px">${b.duration_seconds.toFixed(1)}s — ${(b.final_scores||[0,0])[0]}-${(b.final_scores||[0,0])[1]}</span>
              `}
            </div>
            <div style="display:flex;align-items:center;gap:8px">
              <a href="/replay?gen=${gen}&fighter=${fighterId}&match=${b.match_id}${currentTournament !== 'default' ? '&tournament=' + currentTournament : ''}" target="_blank" onclick="event.stopPropagation()" style="background:var(--accent);color:#fff;padding:2px 10px;border-radius:4px;text-decoration:none;font-size:12px">Watch</a>
              <span class="${b.result}" style="font-weight:700">${b.result.toUpperCase()}</span>
            </div>
          </div>
        `).join('')}
      </div>
    </details>
  `;
}

function showBattleByIndex(idx, fighterId) {
  const b = window._cachedBattles[idx];
  if (b) showBattle(b, fighterId);
}

function showBattle(b, fighterId) {
  if (fighterId) nav.fighterId = fighterId;
  nav = { ...nav, view: 'battle', opponentName: b.opponent, matchId: b.match_id, matchFile: b._filename || '' };
  pushNav();
  renderBreadcrumb();

  const el = document.getElementById('content');
  const actions = Object.entries(b.action_frequency || {}).sort((a,b) => b[1]-a[1]);
  const totalActions = actions.reduce((s,[,v]) => s+v, 0) || 1;

  el.innerHTML = `
    <div style="margin-bottom:12px">
      <a href="/replay?gen=${nav.gen}&fighter=${nav.fighterId}&match=${b.match_id}${currentTournament !== 'default' ? '&tournament=' + currentTournament : ''}" target="_blank" style="background:var(--accent);color:#fff;padding:6px 16px;border-radius:4px;text-decoration:none;font-size:14px;font-weight:600">Watch Replay</a>
      <span style="margin-left:12px;font-size:18px" class="${b.result}"><strong>${b.result.toUpperCase()}</strong></span>
      <span class="subtitle" style="margin-left:8px">${b.duration_seconds.toFixed(1)}s</span>
    </div>

    <div class="card">
      <h2>Final State</h2>
      <div>
        ${b.final_state ? `
          <span class="metric"><span>Fighter HP:</span> ${b.final_state.fighter_hp.toFixed(0)}/100</span>
          <span class="metric"><span>Opponent HP:</span> ${b.final_state.opponent_hp.toFixed(0)}/100</span>
          <span class="metric"><span>Dmg Dealt:</span> ${b.damage_summary.dealt.toFixed(0)}</span>
          <span class="metric"><span>Dmg Received:</span> ${b.damage_summary.received.toFixed(0)}</span>
        ` : `
          <span class="metric"><span>Final Score:</span> ${(b.final_scores||[0,0])[0]} - ${(b.final_scores||[0,0])[1]}</span>
          <span class="metric"><span>Beacon Own%:</span> ${((b.beacon_control?.total_beacon_ownership_pct||0)*100).toFixed(0)}%</span>
          <span class="metric"><span>Captures:</span> ${b.beacon_control?.capture_count || 0}</span>
          <span class="metric"><span>Pulses:</span> ${b.pulse_stats?.pulse_count || 0}</span>
        `}
      </div>
    </div>

    <div class="card">
      <h2>Positional Summary</h2>
      <div>
        <span class="metric"><span>Grounded:</span> ${((b.positional_summary?.time_grounded_pct||0)*100).toFixed(0)}%</span>
        ${b.positional_summary?.time_airborne_pct != null ? `
          <span class="metric"><span>Airborne:</span> ${(b.positional_summary.time_airborne_pct*100).toFixed(0)}%</span>
          <span class="metric"><span>Near Opponent:</span> ${(b.positional_summary.time_near_opponent_pct*100).toFixed(0)}%</span>
          <span class="metric"><span>Avg Distance:</span> ${b.positional_summary.avg_distance_to_opponent.toFixed(0)}px</span>
        ` : `
          <span class="metric"><span>On Platform:</span> ${((b.positional_summary?.time_on_platform_pct||0)*100).toFixed(0)}%</span>
        `}
      </div>
    </div>

    ${b.grapple_stats ? `
    <div class="card">
      <h2>Grapple Stats</h2>
      <div>
        <span class="metric"><span>Attaches:</span> ${b.grapple_stats.attach_count}</span>
        <span class="metric"><span>Ceiling:</span> ${b.grapple_stats.ceiling_attaches}</span>
        <span class="metric"><span>Wall:</span> ${b.grapple_stats.wall_attaches}</span>
        <span class="metric"><span>Avg Duration:</span> ${b.grapple_stats.avg_attached_duration_ticks.toFixed(0)} ticks</span>
      </div>
    </div>
    ` : ''}

    ${b.beacon_control ? `
    <div class="card">
      <h2>Beacon Control</h2>
      <div>
        <span class="metric"><span>Left Owned:</span> ${((b.beacon_control.time_owning_left_pct||0)*100).toFixed(0)}%</span>
        <span class="metric"><span>Center Owned:</span> ${((b.beacon_control.time_owning_center_pct||0)*100).toFixed(0)}%</span>
        <span class="metric"><span>Right Owned:</span> ${((b.beacon_control.time_owning_right_pct||0)*100).toFixed(0)}%</span>
        <span class="metric"><span>Captures:</span> ${b.beacon_control.capture_count || 0}</span>
      </div>
    </div>
    ` : ''}

    ${b.pulse_stats ? `
    <div class="card">
      <h2>Pulse Stats</h2>
      <div>
        <span class="metric"><span>Pulses Used:</span> ${b.pulse_stats.pulse_count}</span>
        <span class="metric"><span>Times Stunned:</span> ${b.pulse_stats.times_stunned}</span>
      </div>
    </div>
    ` : ''}

    <div class="card">
      <h2>Action Frequency</h2>
      ${actions.map(([name, count]) => `
        <div style="margin-bottom:6px">
          <div style="display:flex;justify-content:space-between;font-size:13px;margin-bottom:2px">
            <span>${name}</span><span style="color:var(--dim)">${count} (${(count/totalActions*100).toFixed(0)}%)</span>
          </div>
          <div class="bar"><div class="bar-fill" style="width:${(count/actions[0][1]*100)}%;background:var(--accent)"></div></div>
        </div>
      `).join('')}
    </div>

    ${(b.phase_breakdown||[]).length > 0 ? `
    <div class="card">
      <h2>Phase Breakdown</h2>
      <div class="phase-grid">
        ${b.phase_breakdown.map(p => `
          <div class="phase-card">
            <h4>${p.phase} (tick ${p.tick_range[0]}–${p.tick_range[1]})</h4>
            <div><span class="metric win"><span>Dealt:</span> ${p.damage_dealt.toFixed(0)}</span></div>
            <div><span class="metric loss"><span>Received:</span> ${p.damage_received.toFixed(0)}</span></div>
            <div style="font-size:12px;color:var(--dim);margin-top:4px">HP: ${p.hp_at_end[0].toFixed(0)} vs ${p.hp_at_end[1].toFixed(0)}</div>
          </div>
        `).join('')}
      </div>
    </div>
    ` : ''}

    <div class="card">
      <h2>Key Moments</h2>
      ${(b.key_moments||[]).map(m => `
        <div class="moment">
          <span class="event-type">${m.event}</span>
          <span style="color:var(--dim);font-size:12px;margin-left:8px">tick ${m.tick}</span>
          <div style="font-size:13px;margin-top:2px">${m.description}</div>
        </div>
      `).join('')}
    </div>

    ${(b.hit_log||[]).length > 0 ? `
    <div class="card">
      <h2>Hit Log (${b.hit_log.length} hits)</h2>
      <div class="hit-timeline">
        ${b.hit_log.map(h => `
          <div class="hit-row">
            <span><span class="${h.attacker==='fighter'?'win':'loss'}" style="font-weight:600">${h.attacker}</span> ${h.hand} fist</span>
            <span style="color:var(--dim)">tick ${h.tick} — ${h.damage} dmg</span>
          </div>
        `).join('')}
      </div>
    </div>
    ` : ''}
  `;
}

// ── URL hash navigation ──
function pushNav() {
  const h = new URLSearchParams();
  if (nav.view !== 'home') h.set('view', nav.view);
  if (nav.gen != null) h.set('gen', nav.gen);
  if (nav.fighterId) h.set('fighter', nav.fighterId);
  if (nav.fighterName) h.set('fname', nav.fighterName);
  if (nav.matchId) h.set('match', nav.matchId);
  if (nav.opponentName) h.set('opp', nav.opponentName);
  const hash = h.toString();
  const newUrl = hash ? '#' + hash : location.pathname + location.search;
  if (location.hash.slice(1) !== hash) history.pushState(null, '', newUrl);
}

async function restoreFromHash() {
  const h = new URLSearchParams(location.hash.slice(1));
  const view = h.get('view') || 'home';
  const gen = h.get('gen');
  const fighter = h.get('fighter');
  const fname = h.get('fname');
  // For battle view, restore to fighter page (we can't serialize the full battle object in hash)
  if ((view === 'fighter' || view === 'battle') && gen && fighter) {
    await showFighter(parseInt(gen), fighter, fname || undefined, true);
  } else if (view === 'gen' && gen) {
    await showGeneration(parseInt(gen), true);
  } else {
    await showHome(true);
  }
}

window.addEventListener('popstate', () => restoreFromHash());

loadTournaments().then(() => restoreFromHash());
</script>
</body>
</html>
""";

    private static string BuildTestPageHtml() => """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Tests — AI-BT-Gym</title>
<style>
  :root { --bg: #0d1117; --card: #161b22; --border: #30363d; --text: #e6edf3; --dim: #8b949e; --accent: #58a6ff; --green: #3fb950; --red: #f85149; --yellow: #d29922; }
  * { margin: 0; padding: 0; box-sizing: border-box; }
  body { font-family: 'Segoe UI', system-ui, sans-serif; background: var(--bg); color: var(--text); padding: 20px; }
  h1 { color: var(--accent); margin-bottom: 4px; }
  .subtitle { color: var(--dim); margin-bottom: 16px; }
  .header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
  button { background: var(--accent); color: #fff; border: none; padding: 10px 20px; border-radius: 6px; cursor: pointer; font-size: 14px; font-weight: 600; }
  button:hover { opacity: 0.9; }
  button:disabled { opacity: 0.5; cursor: not-allowed; }
  .status { color: var(--dim); font-size: 14px; margin-left: 12px; }
  .card { background: var(--card); border: 1px solid var(--border); border-radius: 8px; padding: 16px; margin-bottom: 12px; cursor: pointer; }
  .card:hover { border-color: var(--accent); }
  .card h2 { font-size: 15px; margin-bottom: 4px; }
  .pass { color: var(--green); } .fail { color: var(--red); }
  .badge { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 12px; font-weight: 700; }
  .badge-pass { background: rgba(63,185,80,0.15); color: var(--green); }
  .badge-fail { background: rgba(248,81,73,0.15); color: var(--red); }
  .error-msg { color: var(--red); font-size: 13px; margin-top: 4px; }
  canvas { border: 1px solid var(--border); border-radius: 4px; background: #0a0e14; display: block; margin: 12px 0; }
  .controls { display: flex; gap: 8px; align-items: center; margin-bottom: 8px; }
  .controls button { padding: 4px 12px; font-size: 12px; }
  .speed-btn { background: var(--card); border: 1px solid var(--border); color: var(--dim); padding: 3px 8px; font-size: 11px; border-radius: 4px; cursor: pointer; }
  .speed-btn.active { background: var(--accent); color: #fff; border-color: var(--accent); }
  input[type=range] { flex: 1; accent-color: var(--accent); }
  .tick-info { color: var(--dim); font-size: 12px; font-variant-numeric: tabular-nums; min-width: 80px; text-align: right; }
  .metric { display: inline-block; background: var(--bg); padding: 3px 8px; border-radius: 4px; font-size: 12px; }
  .metric span { color: var(--dim); }
  .nav { margin-bottom: 12px; font-size: 13px; }
  .nav a { color: var(--accent); text-decoration: none; } .nav a:hover { text-decoration: underline; }
</style>
</head>
<body>
<div class="nav"><a href="/">Tournaments</a> | <strong>Tests</strong></div>
<div class="header">
  <div><h1>Tests</h1><p class="subtitle">Movement tests with visual replay + Map self-play tests with Godot replay</p></div>
  <div style="display:flex;align-items:center"><button id="runBtn" onclick="runTests()">Run All Tests</button><span id="statusText" class="status"></span></div>
</div>
<h2 style="color:var(--accent);margin:16px 0 8px">Movement Tests</h2>
<div id="results"></div>
<h2 style="color:var(--accent);margin:16px 0 8px">Map Self-Play Tests</h2>
<div id="mapResults"></div>

<script>
let testData = null;
let mapTestData = null;
let activeReplay = null; // {idx, playing, speed, currentTick, maxTick, lastFrameTime, canvas, ctx}

async function runTests() {
  const btn = document.getElementById('runBtn');
  const status = document.getElementById('statusText');
  btn.disabled = true;
  status.textContent = 'Running...';
  try {
    const r = await fetch('/api/tests/run', { method: 'POST' });
    const data = await r.json();
    testData = { passed: data.passed, failed: data.failed, results: data.results };
    mapTestData = data.map_results || [];
    const totalP = data.passed + (data.map_passed || 0);
    const totalF = data.failed + (data.map_failed || 0);
    status.textContent = `${totalP} passed, ${totalF} failed in ${data.elapsed_ms}ms`;
    renderResults();
    renderMapResults();
  } catch(e) { status.textContent = 'Error: ' + e.message; }
  btn.disabled = false;
}

async function loadCached() {
  try {
    const [movR, mapR] = await Promise.all([
      fetch('/api/tests/results'),
      fetch('/api/tests/map-results')
    ]);
    const results = await movR.json();
    if (results.length > 0) {
      const passed = results.filter(r => r.passed).length;
      const failed = results.filter(r => !r.passed).length;
      testData = { passed, failed, results };
      renderResults();
    }
    const mapResults = await mapR.json();
    if (mapResults.length > 0) {
      mapTestData = mapResults;
      renderMapResults();
    }
    // Update combined status
    const tp = (testData?.passed || 0) + (mapTestData ? mapTestData.filter(r => r.passed).length : 0);
    const tf = (testData?.failed || 0) + (mapTestData ? mapTestData.filter(r => !r.passed).length : 0);
    if (tp + tf > 0) document.getElementById('statusText').textContent = `${tp} passed, ${tf} failed (cached)`;
  } catch(e) {}
}

function renderMapResults() {
  if (!mapTestData) return;
  const el = document.getElementById('mapResults');
  el.innerHTML = mapTestData.map(t => {
    const outcome = t.feature_notes?.outcome || '';
    const duration = t.feature_notes?.duration || `${t.duration_ticks} ticks`;
    const notes = Object.entries(t.feature_notes || {})
      .filter(([k]) => k !== 'outcome' && k !== 'duration')
      .map(([k, v]) => `<span class="metric"><span>${k}:</span> ${v}</span>`)
      .join(' ');
    return `
    <div class="card" style="cursor:default">
      <div style="display:flex;justify-content:space-between;align-items:center">
        <h2>${t.map_name}</h2>
        <div style="display:flex;gap:8px;align-items:center">
          ${t.has_replay ? `<button onclick="event.stopPropagation(); launchMapReplay('${t.map_name}')" style="background:#3fb950;padding:6px 14px;font-size:12px">Watch in Godot</button>` : ''}
          <span class="badge ${t.passed ? 'badge-pass' : 'badge-fail'}">${t.passed ? 'PASS' : 'FAIL'}</span>
        </div>
      </div>
      ${t.error ? `<div class="error-msg">${t.error}</div>` : ''}
      <div style="color:var(--dim);font-size:13px;margin-top:4px">${outcome} &mdash; ${duration}</div>
      <div style="margin-top:6px;line-height:1.8">${notes}</div>
    </div>`;
  }).join('');
}

async function launchMapReplay(mapName) {
  try {
    const r = await fetch('/api/tests/map-launch', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ map: mapName })
    });
    const data = await r.json();
    if (data.error) alert('Launch failed: ' + data.error);
  } catch(e) { alert('Launch error: ' + e.message); }
}

function renderResults() {
  if (!testData) return;
  const el = document.getElementById('results');
  el.innerHTML = testData.results.map((t, i) => `
    <div class="card" id="test-${i}" onclick="toggleReplay(${i})">
      <div style="display:flex;justify-content:space-between;align-items:center">
        <h2>${t.name}</h2>
        <span class="badge ${t.passed ? 'badge-pass' : 'badge-fail'}">${t.passed ? 'PASS' : 'FAIL'}</span>
      </div>
      ${t.error ? `<div class="error-msg">${t.error}</div>` : ''}
      <div style="color:var(--dim);font-size:12px;margin-top:4px">${t.duration_ticks} ticks — ${t.replay ? `${t.replay.arena.width}x${t.replay.arena.height} arena` : 'no replay'}</div>
      <div id="replay-${i}"></div>
    </div>
  `).join('');
}

function toggleReplay(idx) {
  const container = document.getElementById('replay-' + idx);
  if (activeReplay && activeReplay.idx === idx) {
    activeReplay.playing = false;
    activeReplay = null;
    container.innerHTML = '';
    return;
  }
  // Close any other open replay
  if (activeReplay) {
    activeReplay.playing = false;
    document.getElementById('replay-' + activeReplay.idx).innerHTML = '';
  }

  const t = testData.results[idx];
  if (!t.replay || t.replay.checkpoints.length === 0) return;
  const r = t.replay;
  const maxTick = t.duration_ticks - 1;

  // 16:9 viewport: height matches arena height, width = height * 16/9
  const viewH = r.arena.height;
  const viewW = viewH * 16 / 9;
  // Canvas pixel size (fit in 800px wide)
  const canvasScale = Math.min(800 / viewW, 500 / viewH, 1);
  const cw = Math.round(viewW * canvasScale);
  const ch = Math.round(viewH * canvasScale);

  container.innerHTML = `
    <canvas id="canvas-${idx}" width="${cw}" height="${ch}"></canvas>
    <div class="controls">
      <button onclick="event.stopPropagation(); toggleTestPlay()">Play</button>
      <button class="speed-btn" onclick="event.stopPropagation(); setTestSpeed(0.5)">0.5x</button>
      <button class="speed-btn active" onclick="event.stopPropagation(); setTestSpeed(1)">1x</button>
      <button class="speed-btn" onclick="event.stopPropagation(); setTestSpeed(2)">2x</button>
      <button class="speed-btn" onclick="event.stopPropagation(); setTestSpeed(4)">4x</button>
      <input type="range" min="0" max="${maxTick}" value="0" onclick="event.stopPropagation()" oninput="event.stopPropagation(); scrubTest(this.value)">
      <span class="tick-info" id="tick-${idx}">0 / ${maxTick}</span>
    </div>`;

  const canvas = document.getElementById('canvas-' + idx);
  const ctx = canvas.getContext('2d');

  activeReplay = { idx, playing: false, speed: 1, currentTick: 0, maxTick, lastFrameTime: 0, canvas, ctx, canvasScale, viewW, viewH, replay: r };
  drawTest();
}

function drawTest() {
  if (!activeReplay) return;
  const { idx, currentTick, maxTick, canvas, ctx, canvasScale, viewW, viewH, replay: r } = activeReplay;
  const tick = Math.min(Math.floor(currentTick), r.checkpoints.length - 1);
  if (tick < 0) return;
  const cp = r.checkpoints[tick];
  const arenaW = r.arena.width, arenaH = r.arena.height, wt = r.arena.wall_thickness;

  // Camera follows fighter (f[0]), centered in the 16:9 viewport
  const fx = cp.f[0].x, fy = cp.f[0].y;
  let camX = fx - viewW / 2;
  let camY = fy - viewH / 2;
  // Clamp camera to arena bounds
  camX = Math.max(0, Math.min(camX, arenaW - viewW));
  camY = Math.max(0, Math.min(camY, arenaH - viewH));

  ctx.save();
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.fillStyle = '#0a0e14';
  ctx.fillRect(0, 0, canvas.width, canvas.height);

  // Scale canvas pixels → world units, then translate for camera
  ctx.scale(canvasScale, canvasScale);
  ctx.translate(-camX, -camY);

  // Grid: minor lines every 50px, major every 200px (only draw visible ones)
  const gridLeft = Math.floor(camX / 50) * 50;
  const gridRight = Math.ceil((camX + viewW) / 50) * 50;
  const gridTop = Math.floor(camY / 50) * 50;
  const gridBottom = Math.ceil((camY + viewH) / 50) * 50;
  for (let gx = gridLeft; gx <= gridRight; gx += 50) {
    if (gx < 0 || gx > arenaW) continue;
    ctx.strokeStyle = gx % 200 === 0 ? '#1a1f26' : '#12161d';
    ctx.lineWidth = gx % 200 === 0 ? 1 : 0.5;
    ctx.beginPath(); ctx.moveTo(gx, Math.max(0, camY)); ctx.lineTo(gx, Math.min(arenaH, camY + viewH)); ctx.stroke();
  }
  for (let gy = gridTop; gy <= gridBottom; gy += 50) {
    if (gy < 0 || gy > arenaH) continue;
    ctx.strokeStyle = gy % 200 === 0 ? '#1a1f26' : '#12161d';
    ctx.lineWidth = gy % 200 === 0 ? 1 : 0.5;
    ctx.beginPath(); ctx.moveTo(Math.max(0, camX), gy); ctx.lineTo(Math.min(arenaW, camX + viewW), gy); ctx.stroke();
  }

  // Arena bounds
  ctx.strokeStyle = '#30363d';
  ctx.lineWidth = 2;
  ctx.strokeRect(wt, wt, arenaW - wt * 2, arenaH - wt * 2);

  // Floor
  ctx.fillStyle = '#444';
  ctx.fillRect(wt, arenaH - wt, arenaW - wt * 2, 2);

  const colors = ['#58a6ff', '#555'];

  // Draw fists & chains
  for (let fi = 0; fi < 4; fi++) {
    const fist = cp.fists[fi];
    const ownerIdx = fi < 2 ? 0 : 1;
    const owner = cp.f[ownerIdx];
    if (fist.s === 0) continue;
    if (ownerIdx === 1) ctx.globalAlpha = 0.2;

    const chainColors = { 1: '#ffd70088', 2: '#58a6ff88', 3: '#66666688' };
    ctx.strokeStyle = chainColors[fist.s] || '#666';
    ctx.lineWidth = 2;
    ctx.beginPath(); ctx.moveTo(owner.x, owner.y); ctx.lineTo(fist.x, fist.y); ctx.stroke();

    if (fist.a) {
      ctx.fillStyle = '#c084fc';
      ctx.beginPath();
      const sz = 5;
      ctx.moveTo(fist.ax, fist.ay - sz); ctx.lineTo(fist.ax + sz, fist.ay);
      ctx.lineTo(fist.ax, fist.ay + sz); ctx.lineTo(fist.ax - sz, fist.ay);
      ctx.closePath(); ctx.fill();
    }

    const fistColors = { 0: '#444', 1: '#ffd700', 2: '#58a6ff', 3: '#666' };
    ctx.fillStyle = fistColors[fist.s] || '#888';
    ctx.beginPath(); ctx.arc(fist.x, fist.y, 8, 0, Math.PI * 2); ctx.fill();
    ctx.globalAlpha = 1;
  }

  // Draw fighters
  for (let i = 0; i < 2; i++) {
    const f = cp.f[i];
    ctx.fillStyle = colors[i];
    ctx.globalAlpha = i === 1 ? 0.1 : 0.3;
    ctx.beginPath(); ctx.arc(f.x, f.y, 18, 0, Math.PI * 2); ctx.fill();
    ctx.globalAlpha = i === 1 ? 0.2 : 1;
    ctx.strokeStyle = colors[i];
    ctx.lineWidth = 2;
    ctx.stroke();
    ctx.globalAlpha = 1;
  }

  // Tick label (in screen space)
  ctx.translate(camX, camY); // back to viewport-relative
  ctx.fillStyle = '#8b949e';
  ctx.font = '12px monospace';
  ctx.fillText(`Tick ${tick} / ${maxTick}`, 8, viewH - 8);

  ctx.restore();

  document.getElementById('tick-' + idx).textContent = `${tick} / ${maxTick}`;
  const scrubber = document.querySelector('#replay-' + idx + ' input[type=range]');
  if (scrubber) scrubber.value = tick;
}

function toggleTestPlay() {
  if (!activeReplay) return;
  activeReplay.playing = !activeReplay.playing;
  if (activeReplay.playing) {
    if (activeReplay.currentTick >= activeReplay.maxTick) activeReplay.currentTick = 0;
    activeReplay.lastFrameTime = performance.now();
    requestAnimationFrame(animateTest);
  }
}

function setTestSpeed(s) {
  if (!activeReplay) return;
  activeReplay.speed = s;
  const btns = document.querySelectorAll('#replay-' + activeReplay.idx + ' .speed-btn');
  btns.forEach(b => b.classList.toggle('active', parseFloat(b.textContent) === s));
}

function scrubTest(val) {
  if (!activeReplay) return;
  activeReplay.currentTick = parseInt(val);
  drawTest();
}

function animateTest(now) {
  if (!activeReplay || !activeReplay.playing) return;
  const elapsed = (now - activeReplay.lastFrameTime) / 1000;
  activeReplay.lastFrameTime = now;
  activeReplay.currentTick += elapsed * 60 * activeReplay.speed;
  if (activeReplay.currentTick >= activeReplay.maxTick) {
    activeReplay.currentTick = activeReplay.maxTick;
    activeReplay.playing = false;
  }
  drawTest();
  if (activeReplay.playing) requestAnimationFrame(animateTest);
}

loadCached();
</script>
</body>
</html>
""";

    private static string BuildReplayViewerHtml() => """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Match Replay — AI-BT-Gym</title>
<style>
  :root { --bg: #0d1117; --card: #161b22; --border: #30363d; --text: #e6edf3; --dim: #8b949e; --accent: #58a6ff; --green: #3fb950; --red: #f85149; --yellow: #d29922; }
  * { margin: 0; padding: 0; box-sizing: border-box; }
  body { font-family: 'Segoe UI', system-ui, sans-serif; background: var(--bg); color: var(--text); display: flex; flex-direction: column; align-items: center; padding: 16px; }
  h1 { color: var(--accent); font-size: 18px; margin-bottom: 4px; }
  .subtitle { color: var(--dim); font-size: 13px; margin-bottom: 12px; }
  canvas { border: 1px solid var(--border); border-radius: 4px; background: #0a0e14; }
  .controls { display: flex; gap: 8px; align-items: center; margin-top: 10px; width: 100%; max-width: 1200px; }
  button { background: var(--accent); color: #fff; border: none; padding: 6px 14px; border-radius: 4px; cursor: pointer; font-size: 13px; font-weight: 600; }
  button:hover { opacity: 0.9; }
  .speed-btn { background: var(--card); border: 1px solid var(--border); color: var(--dim); padding: 4px 10px; font-size: 12px; }
  .speed-btn.active { background: var(--accent); color: #fff; border-color: var(--accent); }
  input[type=range] { flex: 1; accent-color: var(--accent); }
  .info { color: var(--dim); font-size: 12px; font-variant-numeric: tabular-nums; min-width: 120px; text-align: right; }
  .hud { display: flex; justify-content: space-between; width: 100%; max-width: 1200px; margin-top: 8px; font-size: 13px; }
  .hud-fighter { padding: 8px 12px; background: var(--card); border: 1px solid var(--border); border-radius: 4px; min-width: 200px; }
  .hp-bar { height: 8px; background: var(--border); border-radius: 4px; margin-top: 4px; overflow: hidden; }
  .hp-fill { height: 100%; border-radius: 4px; transition: width 0.1s; }
  .loading { color: var(--dim); margin-top: 100px; font-size: 16px; }
  #error { color: var(--red); margin-top: 12px; }
  .breadcrumb { font-size: 13px; color: var(--dim); margin-bottom: 10px; width: 100%; max-width: 1200px; display: flex; align-items: center; flex-wrap: wrap; }
  .breadcrumb a { color: var(--accent); text-decoration: none; }
  .breadcrumb a:hover { text-decoration: underline; }
  .breadcrumb .sep { margin: 0 6px; color: var(--border); }
  .breadcrumb .current { color: var(--text); font-weight: 600; }
  .copy-path { margin-left: 10px; background: var(--card); border: 1px solid var(--border); color: var(--dim); padding: 2px 8px; border-radius: 4px; cursor: pointer; font-size: 12px; font-family: monospace; white-space: nowrap; }
  .copy-path:hover { color: var(--accent); border-color: var(--accent); }
  .match-id { display: inline-block; margin-top: 4px; padding: 3px 10px; background: var(--card); border: 1px solid var(--border); border-radius: 4px; font-family: 'Consolas', 'Fira Code', monospace; font-size: 13px; color: var(--accent); user-select: all; -webkit-user-select: all; cursor: text; letter-spacing: 0.3px; }
</style>
</head>
<body>
<div id="breadcrumb" class="breadcrumb"></div>
<span class="match-id" id="matchIdLabel"></span>
<p class="subtitle" id="matchInfo">Loading...</p>
<canvas id="arena" width="1500" height="680"></canvas>
<div class="controls">
  <button id="playBtn" onclick="togglePlay()">Play</button>
  <button class="speed-btn" onclick="setSpeed(0.5)">0.5x</button>
  <button class="speed-btn active" onclick="setSpeed(1)">1x</button>
  <button class="speed-btn" onclick="setSpeed(2)">2x</button>
  <button class="speed-btn" onclick="setSpeed(4)">4x</button>
  <button id="launchGodotBtn" style="background:#3fb950;margin-left:8px" onclick="launchInGodot()">Launch in Godot</button>
  <input type="range" id="scrubber" min="0" max="100" value="0" oninput="scrub(this.value)">
  <span class="info" id="tickInfo">0 / 0</span>
</div>
<div class="hud">
  <div class="hud-fighter">
    <div><span style="color:#f85149;font-weight:700" id="name0">Fighter 0</span> <span id="hp0text">100</span> <span id="unit0">HP</span></div>
    <div class="hp-bar"><div class="hp-fill" id="hp0bar" style="width:100%;background:var(--green)"></div></div>
  </div>
  <div class="hud-fighter" style="text-align:right">
    <div><span style="color:#58a6ff;font-weight:700" id="name1">Fighter 1</span> <span id="hp1text">100</span> <span id="unit1">HP</span></div>
    <div class="hp-bar"><div class="hp-fill" id="hp1bar" style="width:100%;background:var(--green)"></div></div>
  </div>
</div>
<div id="error"></div>

<script>
const canvas = document.getElementById('arena');
const ctx = canvas.getContext('2d');
const params = new URLSearchParams(location.search);
const gen = params.get('gen');
const fighter = params.get('fighter');
const matchId = params.get('match');
const tournament = params.get('tournament') || 'default';
const apiPrefix = tournament === 'default' ? '/api' : `/api/tournaments/${tournament}`;

let data = null; // full battle log with replay
let replay = null;
let playing = false;
let speed = 1;
let currentTick = 0;
let maxTick = 0;
let lastFrameTime = null;
let hitFlashes = []; // [{tick, x, y, ttl}]
let isBeaconBrawl = false;
let teamColors = []; // per-pawn colors for beacon brawl
let rifleShots = []; // [{tk, pi, sg, h, hp}] — rifle shot events from replay data
let hitEvents = [];  // [{tk, pi}] — pawn hit events for flash rendering

// Colors (defaults, overridden by fighter_color/opponent_color from battle log)
let F0_COLOR = '#f85149';
let F1_COLOR = '#58a6ff';
const FIST_COLORS = { 0: '#444', 1: '#ffd700', 2: '#58a6ff', 3: '#666' }; // retracted, extending, locked, retracting
const CHAIN_COLORS = { 0: 'transparent', 1: '#ffd70088', 2: '#58a6ff88', 3: '#66666688' };
const ANCHOR_COLOR = '#c084fc';
const ARENA_COLOR = '#30363d';
const FLOOR_COLOR = '#444';

async function findFighterDir(gen, fighterId) {
  try {
    const fighters = await (await fetch(`${apiPrefix}/generations/${gen}/fighters`)).json();
    const f = fighters.find(f => f.fighter_id === fighterId);
    return f ? `${fighterId}_${f.name}` : fighterId;
  } catch { return fighterId; }
}

async function load() {
  if (!gen || !fighter || !matchId) {
    document.getElementById('error').textContent = 'Missing query params: gen, fighter, match';
    return;
  }

  try {
    // Find the battle file
    const battles = await (await fetch(`${apiPrefix}/generations/${gen}/fighters/${fighter}/battles`)).json();
    const battle = battles.find(b => b.match_id === matchId);
    if (!battle) { document.getElementById('error').textContent = 'Battle not found'; return; }
    if (!battle.replay) { document.getElementById('error').textContent = 'No replay data in this battle log (re-run tournament)'; return; }

    data = battle;
    replay = battle.replay;
    maxTick = battle.duration_ticks;
    rifleShots = replay.rifle_shots || [];
    hitEvents = replay.hit_events || [];

    // Detect beacon brawl: checkpoints have 'p' (pawns) array instead of fighters in 'f'
    isBeaconBrawl = replay.checkpoints.length > 0 && replay.checkpoints[0].p != null;

    // Apply fighter/team colors from battle log
    if (battle.fighter_color || battle.team_color) F0_COLOR = battle.fighter_color || battle.team_color;
    if (battle.opponent_color) F1_COLOR = battle.opponent_color;

    // Build per-pawn color array for beacon brawl
    if (isBeaconBrawl && replay.checkpoints.length > 0) {
      const cp = replay.checkpoints[0];
      teamColors = cp.p.map(p => p.t === 0 ? F0_COLOR : F1_COLOR);
    }

    const teamName = battle.fighter || battle.team || 'Team A';
    const oppName = battle.opponent || 'Team B';
    const scoreStr = battle.final_scores ? ` (${battle.final_scores[0]}-${battle.final_scores[1]})` : '';
    document.getElementById('matchInfo').textContent = `${teamName} vs ${oppName} — ${battle.result.toUpperCase()} in ${battle.duration_seconds.toFixed(1)}s${scoreStr}`;

    // Build relative file path for copy button
    const pad = n => String(n).padStart(3, '0');
    const fighterDir = await findFighterDir(gen, fighter);
    const filename = battle._filename || '';
    const base = tournament === 'default' ? 'generations' : `tournaments/${tournament}`;
    const relPath = filename
      ? `${base}/gen_${pad(parseInt(gen))}/fighters/${fighterDir}/battles/${filename}`
      : `${base}/gen_${pad(parseInt(gen))}/fighters/${fighterDir}/battles`;

    // Breadcrumb
    const bc = document.getElementById('breadcrumb');
    const tq = tournament !== 'default' ? `?tournament=${tournament}` : '';
    bc.innerHTML = `<a href="/${tq}">Tournaments</a><span class="sep">&gt;</span>`
      + `<a href="/${tq}#gen=${gen}">Gen_${gen}</a><span class="sep">&gt;</span>`
      + `<a href="/${tq}#gen=${gen}&fighter=${fighter}">${battle.fighter}</a><span class="sep">&gt;</span>`
      + `<span>vs ${battle.opponent}</span><span class="sep">&gt;</span>`
      + `<span class="current">Replay</span>`
      + `<button class="copy-path" title="${relPath}" onclick="(() => { navigator.clipboard.writeText('${relPath}'); this.textContent='copied!'; setTimeout(() => this.textContent='${relPath}', 1200); })()">${relPath}</button>`;
    // Build selectable match ID label
    const tAbbrev = tournament === 'beacon_brawl' ? 'bb' : tournament === 'default' ? 'df' : tournament.replace(/[^a-z0-9]/gi, '').substring(0, 6);
    const colorOf = s => (s || '').toLowerCase().split(/[_ ]/)[0];
    const matchNum = (matchId || '').replace(/.*match[_]?0*(\d+).*/i, '$1') || matchId;
    const matchIdLabel = `${tAbbrev}.gen_${gen}.${colorOf(teamName)}.${colorOf(oppName)}.match${matchNum}`;
    document.getElementById('matchIdLabel').textContent = matchIdLabel;

    document.getElementById('name0').textContent = teamName;
    document.getElementById('name0').style.color = F0_COLOR;
    document.getElementById('name1').textContent = oppName;
    document.getElementById('name1').style.color = F1_COLOR;
    if (isBeaconBrawl) {
      document.getElementById('unit0').textContent = 'pts';
      document.getElementById('unit1').textContent = 'pts';
      document.getElementById('hp0text').textContent = '0';
      document.getElementById('hp1text').textContent = '0';
    }
    document.getElementById('scrubber').max = maxTick;

    // Scale arena to fit canvas (max 1500px wide)
    const arenaW = replay.arena.width;
    const arenaH = replay.arena.height;
    const viewScale = Math.min(1500 / arenaW, 600 / arenaH, 1);
    canvas.width = Math.round(arenaW * viewScale);
    canvas.height = Math.round(arenaH * viewScale);
    canvas.dataset.viewScale = viewScale;

    draw();
  } catch(e) {
    document.getElementById('error').textContent = 'Load error: ' + e.message;
  }
}

function getState(tick) {
  if (!replay || replay.checkpoints.length === 0) return null;
  const interval = replay.checkpoint_interval;
  const idx = tick / interval;
  const lo = Math.floor(idx);
  const hi = Math.ceil(idx);
  const cpLen = replay.checkpoints.length;

  if (lo >= cpLen - 1) return isBeaconBrawl ? normalizeBB(replay.checkpoints[cpLen - 1]) : normalizeFight(replay.checkpoints[cpLen - 1]);
  if (hi >= cpLen) return isBeaconBrawl ? normalizeBB(replay.checkpoints[cpLen - 1]) : normalizeFight(replay.checkpoints[cpLen - 1]);

  const a = replay.checkpoints[lo];
  const b = replay.checkpoints[hi];
  const frac = idx - lo;

  if (isBeaconBrawl) return interpolateBB(a, b, frac, tick);
  return interpolateFight(a, b, frac, tick);
}

function normalizeFight(cp) {
  return { t: cp.t, f: cp.f, fists: cp.fists };
}

function interpolateFight(a, b, frac, tick) {
  return {
    t: tick,
    f: [0, 1].map(i => ({
      x: lerp(a.f[i].x, b.f[i].x, frac),
      y: lerp(a.f[i].y, b.f[i].y, frac),
      vx: lerp(a.f[i].vx, b.f[i].vx, frac),
      vy: lerp(a.f[i].vy, b.f[i].vy, frac),
      hp: lerp(a.f[i].hp, b.f[i].hp, frac),
      g: frac < 0.5 ? a.f[i].g : b.f[i].g
    })),
    fists: [0, 1, 2, 3].map(i => ({
      s: frac < 0.5 ? a.fists[i].s : b.fists[i].s,
      x: lerp(a.fists[i].x, b.fists[i].x, frac),
      y: lerp(a.fists[i].y, b.fists[i].y, frac),
      ax: a.fists[i].a ? a.fists[i].ax : b.fists[i].ax,
      ay: a.fists[i].a ? a.fists[i].ay : b.fists[i].ay,
      cl: lerp(a.fists[i].cl, b.fists[i].cl, frac),
      a: frac < 0.5 ? a.fists[i].a : b.fists[i].a
    }))
  };
}

function normalizeBB(cp) {
  return {
    t: cp.t, scores: cp.s, beacons: cp.b,
    pawns: cp.p,
    fists: cp.f.map(fi => ({...fi})),
    projectiles: cp.pr || []
  };
}

function interpolateBB(a, b, frac, tick) {
  return {
    t: tick,
    scores: frac < 0.5 ? a.s : b.s,
    beacons: frac < 0.5 ? a.b : b.b,
    pawns: a.p.map((pa, i) => ({
      t: pa.t,
      r: pa.r,   // role: 0=Grappler, 1=Gunner
      x: lerp(pa.x, b.p[i].x, frac),
      y: lerp(pa.y, b.p[i].y, frac),
      vx: lerp(pa.vx, b.p[i].vx, frac),
      vy: lerp(pa.vy, b.p[i].vy, frac),
      hp: lerp(pa.hp, b.p[i].hp, frac),
      g:  frac < 0.5 ? pa.g  : b.p[i].g,
      d:  frac < 0.5 ? pa.d  : b.p[i].d,
      v:  frac < 0.5 ? pa.v  : b.p[i].v,
      pa: frac < 0.5 ? pa.pa : b.p[i].pa,
      wl: frac < 0.5 ? pa.wl : b.p[i].wl
    })),
    fists: a.f.map((fa, i) => ({
      pi: fa.pi,  // pawn owner index in AllPawns — constant, don't interpolate
      s:  frac < 0.5 ? fa.s : b.f[i].s,
      x:  lerp(fa.x, b.f[i].x, frac),
      y:  lerp(fa.y, b.f[i].y, frac),
      ax: fa.a ? fa.ax : b.f[i].ax,
      ay: fa.a ? fa.ay : b.f[i].ay,
      cl: lerp(fa.cl, b.f[i].cl, frac),
      a:  frac < 0.5 ? fa.a : b.f[i].a
    })),
    projectiles: frac < 0.5 ? (a.pr || []) : (b.pr || [])
  };
}

function lerp(a, b, t) { return a + (b - a) * t; }

function drawFight(state, w, h, vs) {
  const fighterColors = [F0_COLOR, F1_COLOR];
  for (let fi = 0; fi < 4; fi++) {
    const fist = state.fists[fi];
    const ownerIdx = fi < 2 ? 0 : 1;
    const owner = state.f[ownerIdx];
    if (fist.s === 0) continue;
    ctx.strokeStyle = CHAIN_COLORS[fist.s] || '#666';
    ctx.lineWidth = 2;
    ctx.beginPath(); ctx.moveTo(owner.x, owner.y); ctx.lineTo(fist.x, fist.y); ctx.stroke();
    if (fist.a) {
      ctx.fillStyle = ANCHOR_COLOR;
      ctx.beginPath();
      const ax = fist.ax, ay = fist.ay, sz = 5;
      ctx.moveTo(ax, ay - sz); ctx.lineTo(ax + sz, ay); ctx.lineTo(ax, ay + sz); ctx.lineTo(ax - sz, ay); ctx.closePath(); ctx.fill();
    }
    ctx.fillStyle = FIST_COLORS[fist.s] || '#888';
    ctx.beginPath(); ctx.arc(fist.x, fist.y, 8, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = fighterColors[ownerIdx]; ctx.lineWidth = 1; ctx.stroke();
  }
  for (let i = 0; i < 2; i++) {
    const f = state.f[i];
    ctx.fillStyle = fighterColors[i]; ctx.globalAlpha = 0.3;
    ctx.beginPath(); ctx.arc(f.x, f.y, 18, 0, Math.PI * 2); ctx.fill();
    ctx.globalAlpha = 1; ctx.strokeStyle = fighterColors[i]; ctx.lineWidth = 2; ctx.stroke();
    const barW = 36, barH = 4, barY = f.y - 28;
    ctx.fillStyle = '#333'; ctx.fillRect(f.x - barW/2, barY, barW, barH);
    const hpPct = Math.max(0, f.hp / 100);
    const hpColor = hpPct > 0.5 ? '#3fb950' : hpPct > 0.25 ? '#d29922' : '#f85149';
    ctx.fillStyle = hpColor; ctx.fillRect(f.x - barW/2, barY, barW * hpPct, barH);
  }
  const activeHits = (data.hit_log || []).filter(h => Math.abs(h.tick - currentTick) < 8);
  for (const h of activeHits) {
    const age = Math.abs(currentTick - h.tick); const alpha = 1 - age / 8;
    const pos = h.attacker === 'fighter' ? h.opponent_pos : h.fighter_pos;
    ctx.fillStyle = `rgba(255, 255, 100, ${alpha * 0.6})`;
    ctx.beginPath(); ctx.arc(pos[0], pos[1], 24 - age * 2, 0, Math.PI * 2); ctx.fill();
  }
}

function drawBeaconBrawl(state, w, h, vs) {
  const ar = replay.arena;
  const RIFLE_SHOW_TICKS = 14;
  const HIT_FLASH_TICKS = 8;
  const BEACON_COLORS = { 0: '#555', 1: F0_COLOR, 2: F1_COLOR };

  // Platform
  if (ar.platform_rect) {
    const [px, py, pw, ph] = ar.platform_rect;
    ctx.fillStyle = '#2a3040';
    ctx.fillRect(px, py, pw, ph);
    ctx.strokeStyle = '#4a5568'; ctx.lineWidth = 2; ctx.strokeRect(px, py, pw, ph);
  }

  // Beacon zones
  if (state.beacons && ar.beacon_centers) {
    for (let i = 0; i < state.beacons.length; i++) {
      const bc = ar.beacon_centers[i];
      const bs = state.beacons[i];
      const bx = bc[0], by = bc[1], br = 80;

      ctx.globalAlpha = 0.12;
      ctx.fillStyle = BEACON_COLORS[bs.o] || '#555';
      ctx.beginPath(); ctx.arc(bx, by, br, 0, Math.PI * 2); ctx.fill();
      ctx.globalAlpha = 1;

      ctx.strokeStyle = BEACON_COLORS[bs.o] || '#555';
      ctx.lineWidth = 2; ctx.globalAlpha = 0.6;
      ctx.setLineDash([8, 6]);
      ctx.beginPath(); ctx.arc(bx, by, br, 0, Math.PI * 2); ctx.stroke();
      ctx.setLineDash([]);
      ctx.globalAlpha = 1;

      if (bs.cp > 0) {
        const progress = bs.cp / 90;
        ctx.strokeStyle = BEACON_COLORS[bs.o === 0 ? 1 : bs.o] || '#ffd700';
        ctx.lineWidth = 3;
        ctx.beginPath(); ctx.arc(bx, by, br + 4, -Math.PI/2, -Math.PI/2 + progress * Math.PI * 2); ctx.stroke();
      }

      if (bs.ct) {
        ctx.strokeStyle = '#ffd700'; ctx.lineWidth = 2; ctx.globalAlpha = 0.7;
        ctx.beginPath(); ctx.moveTo(bx - 8, by - 8); ctx.lineTo(bx + 8, by + 8); ctx.stroke();
        ctx.beginPath(); ctx.moveTo(bx + 8, by - 8); ctx.lineTo(bx - 8, by + 8); ctx.stroke();
        ctx.globalAlpha = 1;
      }

      const mult = ar.beacon_multipliers ? ar.beacon_multipliers[i] : (i === 1 ? 3 : 1);
      if (mult > 1) {
        ctx.fillStyle = '#ffd70088'; ctx.font = `${11/vs}px monospace`;
        ctx.fillText(`${mult}x`, bx - 8, by - br - 6);
      }
    }
  }

  // Rifle shot paths (dotted lines — drawn under pawns)
  for (const shot of rifleShots) {
    const age = currentTick - shot.tk;
    if (age < 0 || age >= RIFLE_SHOW_TICKS) continue;
    const alpha = 1 - age / RIFLE_SHOW_TICKS;
    const segs = shot.sg || [];
    if (segs.length < 2) continue;
    ctx.globalAlpha = alpha;
    ctx.strokeStyle = '#ffd700';
    ctx.lineWidth = 1.5;
    ctx.setLineDash([5, 5]);
    ctx.beginPath();
    for (let si = 0; si < segs.length - 1; si++) {
      ctx.moveTo(segs[si][0], segs[si][1]);
      ctx.lineTo(segs[si+1][0], segs[si+1][1]);
    }
    ctx.stroke();
    ctx.setLineDash([]);
    if (shot.h) {
      const last = segs[segs.length - 1];
      ctx.fillStyle = '#ff8800';
      ctx.beginPath(); ctx.arc(last[0], last[1], 4, 0, Math.PI * 2); ctx.fill();
    }
    ctx.globalAlpha = 1;
  }

  // Grappling hooks / tethers
  for (let fi = 0; fi < state.fists.length; fi++) {
    const fist = state.fists[fi];
    const ownerIdx = fist.pi !== undefined ? fist.pi : Math.floor(fi / 2);
    if (ownerIdx >= state.pawns.length) continue;
    const owner = state.pawns[ownerIdx];
    if (fist.s === 0) continue;

    ctx.strokeStyle = CHAIN_COLORS[fist.s] || '#666'; ctx.lineWidth = 2;
    ctx.beginPath(); ctx.moveTo(owner.x, owner.y); ctx.lineTo(fist.x, fist.y); ctx.stroke();

    if (fist.a) {
      ctx.fillStyle = ANCHOR_COLOR;
      const ax = fist.ax, ay = fist.ay, sz = 5;
      ctx.beginPath();
      ctx.moveTo(ax, ay - sz); ctx.lineTo(ax + sz, ay); ctx.lineTo(ax, ay + sz); ctx.lineTo(ax - sz, ay);
      ctx.closePath(); ctx.fill();
    }

    ctx.fillStyle = FIST_COLORS[fist.s] || '#888';
    ctx.beginPath(); ctx.arc(fist.x, fist.y, 8, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = teamColors[ownerIdx] || '#888'; ctx.lineWidth = 1; ctx.stroke();
  }

  // Projectiles (pistol bullets)
  for (const proj of (state.projectiles || [])) {
    const pcolor = proj.t === 0 ? F0_COLOR : F1_COLOR;
    ctx.fillStyle = pcolor; ctx.globalAlpha = 0.9;
    ctx.beginPath(); ctx.arc(proj.x, proj.y, 4, 0, Math.PI * 2); ctx.fill();
    ctx.globalAlpha = 1;
    ctx.strokeStyle = '#ffffff44'; ctx.lineWidth = 1; ctx.stroke();
  }

  // Pawns
  for (let i = 0; i < state.pawns.length; i++) {
    const p = state.pawns[i];
    const color = teamColors[i] || (p.t === 0 ? F0_COLOR : F1_COLOR);
    const isDead = !!p.d;

    if (isDead) {
      // Ghost: dashed translucent ring
      ctx.globalAlpha = 0.2;
      ctx.strokeStyle = color; ctx.lineWidth = 1.5;
      ctx.setLineDash([4, 4]);
      ctx.beginPath(); ctx.arc(p.x, p.y, 16, 0, Math.PI * 2); ctx.stroke();
      ctx.setLineDash([]);
      ctx.globalAlpha = 1;
      continue;
    }

    // Hit flash (white burst on recent hit)
    let flashAlpha = 0;
    for (let hi = hitEvents.length - 1; hi >= 0; hi--) {
      const he = hitEvents[hi];
      if (he.pi === i) {
        const age = currentTick - he.tk;
        if (age >= 0 && age < HIT_FLASH_TICKS) flashAlpha = 1 - age / HIT_FLASH_TICKS;
        break;
      }
    }
    if (flashAlpha > 0) {
      ctx.fillStyle = `rgba(255,255,200,${flashAlpha * 0.65})`;
      ctx.beginPath(); ctx.arc(p.x, p.y, 22, 0, Math.PI * 2); ctx.fill();
    }

    // Debuff outlines (drawn before body so body appears on top)
    ctx.globalAlpha = 0.85;
    if (p.v) { // vulnerable — red ring
      ctx.strokeStyle = '#ff4444'; ctx.lineWidth = 2;
      ctx.beginPath(); ctx.arc(p.x, p.y, 23, 0, Math.PI * 2); ctx.stroke();
    }
    if (p.wl) { // weapon locked (parry lockout) — orange ring
      ctx.strokeStyle = '#ff8800'; ctx.lineWidth = 2;
      ctx.beginPath(); ctx.arc(p.x, p.y, 25, 0, Math.PI * 2); ctx.stroke();
    }
    if (p.pa) { // parry active — bright cyan ring
      ctx.strokeStyle = '#00ffee'; ctx.lineWidth = 2.5;
      ctx.globalAlpha = 0.95;
      ctx.beginPath(); ctx.arc(p.x, p.y, 20, 0, Math.PI * 2); ctx.stroke();
    }
    ctx.globalAlpha = 1;

    // Body
    ctx.fillStyle = color; ctx.globalAlpha = 0.35;
    ctx.beginPath(); ctx.arc(p.x, p.y, 16, 0, Math.PI * 2); ctx.fill();
    ctx.globalAlpha = 1; ctx.strokeStyle = color; ctx.lineWidth = 2; ctx.stroke();

    // HP bar (above pawn)
    const barW = 36, barH = 4, barY = p.y - 28;
    const hpPct = Math.max(0, (p.hp || 0) / 100);
    ctx.fillStyle = '#333'; ctx.fillRect(p.x - barW/2, barY, barW, barH);
    ctx.fillStyle = hpPct > 0.5 ? '#3fb950' : hpPct > 0.25 ? '#d29922' : '#f85149';
    ctx.fillRect(p.x - barW/2, barY, barW * hpPct, barH);

    // Role indicator label (G / R)
    ctx.fillStyle = color + 'cc'; ctx.font = `bold ${9/vs}px monospace`;
    ctx.fillText(p.r === 0 ? 'G' : 'R', p.x - 4, p.y + 5);

    // ── Role-specific weapon rendering ──

    if (p.r === 0) {
      // Grappler: fists at ready when hook is not extended
      const hookEntry = state.fists.find(f => (f.pi !== undefined ? f.pi : -1) === i);
      const hookRetracted = !hookEntry || hookEntry.s === 0;
      if (hookRetracted) {
        const facing = p.t === 0 ? 1 : -1;
        const fistOffsets = [[-10 * facing - 8, 7], [-10 * facing + 8, 7]];
        for (const [ox, oy] of fistOffsets) {
          ctx.fillStyle = color; ctx.globalAlpha = 0.8;
          ctx.beginPath(); ctx.arc(p.x + ox, p.y + oy, 4, 0, Math.PI * 2); ctx.fill();
          ctx.globalAlpha = 1;
          ctx.strokeStyle = '#ffffff44'; ctx.lineWidth = 1; ctx.stroke();
        }
      }
    } else {
      // Gunner: rifle line (slung by default, pops to aim direction when fired)
      // Find most recent rifle shot from this pawn
      let rifleAngle = p.t === 0 ? 0.5 : Math.PI - 0.5; // slung: forward+down
      let rifleActive = false;
      for (let ri = rifleShots.length - 1; ri >= 0; ri--) {
        const rs = rifleShots[ri];
        if (rs.pi === i && rs.tk <= currentTick && currentTick - rs.tk < 20) {
          const segs = rs.sg || [];
          if (segs.length >= 2) {
            rifleAngle = Math.atan2(segs[1][1] - segs[0][1], segs[1][0] - segs[0][0]);
            rifleActive = (currentTick - rs.tk) < 10;
          }
          break;
        }
      }
      const rLen = 18;
      const rx = p.x + Math.cos(rifleAngle) * rLen;
      const ry = p.y + Math.sin(rifleAngle) * rLen;
      ctx.strokeStyle = rifleActive ? '#ffd700' : '#6a7a8a';
      ctx.lineWidth = rifleActive ? 3 : 2;
      ctx.beginPath(); ctx.moveTo(p.x, p.y); ctx.lineTo(rx, ry); ctx.stroke();
      ctx.fillStyle = rifleActive ? '#ffd700' : '#8a9aaa';
      ctx.beginPath(); ctx.arc(rx, ry, 2.5, 0, Math.PI * 2); ctx.fill();
    }
  }

  // Scores on canvas
  if (state.scores) {
    ctx.fillStyle = F0_COLOR; ctx.font = `bold ${18/vs}px monospace`;
    ctx.fillText(`${state.scores[0]}`, 30, 30);
    ctx.fillStyle = '#8b949e'; ctx.fillText(' - ', 60, 30);
    ctx.fillStyle = F1_COLOR; ctx.fillText(`${state.scores[1]}`, 90, 30);
    ctx.fillStyle = '#555'; ctx.font = `${11/vs}px monospace`;
    ctx.fillText(`/ ${data.final_scores ? Math.max(...data.final_scores) : 30}`, 120, 30);
  }
}

function draw() {
  if (!replay) return;
  const state = getState(currentTick);
  if (!state) return;

  const w = replay.arena.width, h = replay.arena.height;
  const wt = replay.arena.wall_thickness;
  const vs = parseFloat(canvas.dataset.viewScale) || 1;

  // Clear
  ctx.fillStyle = '#0a0e14';
  ctx.fillRect(0, 0, canvas.width, canvas.height);

  ctx.save();
  ctx.scale(vs, vs);

  // Grid: minor lines every 50px, major every 200px
  for (let gx = 0; gx <= w; gx += 50) {
    ctx.strokeStyle = gx % 200 === 0 ? '#1a1f26' : '#12161d';
    ctx.lineWidth = (gx % 200 === 0 ? 1 : 0.5) / vs;
    ctx.beginPath(); ctx.moveTo(gx, 0); ctx.lineTo(gx, h); ctx.stroke();
  }
  for (let gy = 0; gy <= h; gy += 50) {
    ctx.strokeStyle = gy % 200 === 0 ? '#1a1f26' : '#12161d';
    ctx.lineWidth = (gy % 200 === 0 ? 1 : 0.5) / vs;
    ctx.beginPath(); ctx.moveTo(0, gy); ctx.lineTo(w, gy); ctx.stroke();
  }

  // Arena bounds
  ctx.strokeStyle = ARENA_COLOR;
  ctx.lineWidth = 2 / vs;
  ctx.strokeRect(wt, wt, w - wt * 2, h - wt * 2);

  // Floor highlight
  ctx.fillStyle = FLOOR_COLOR;
  ctx.fillRect(wt, h - wt, w - wt * 2, 2);

  if (isBeaconBrawl) {
    drawBeaconBrawl(state, w, h, vs);
  } else {
    drawFight(state, w, h, vs);
  }

  // Tick counter on canvas
  ctx.fillStyle = '#8b949e';
  ctx.font = `${12/vs}px monospace`;
  ctx.fillText(`Tick ${Math.floor(currentTick)} / ${maxTick}`, 20, h - 8);

  ctx.restore();

  // Update HUD
  if (isBeaconBrawl && state.scores) {
    document.getElementById('hp0text').textContent = state.scores[0];
    document.getElementById('hp1text').textContent = state.scores[1];
    const target = data.final_scores ? Math.max(...data.final_scores) : 30;
    const bar0 = document.getElementById('hp0bar');
    const bar1 = document.getElementById('hp1bar');
    bar0.style.width = Math.min(100, state.scores[0] / target * 100) + '%';
    bar0.style.background = 'var(--green)';
    bar1.style.width = Math.min(100, state.scores[1] / target * 100) + '%';
    bar1.style.background = 'var(--green)';
  } else if (state.f) {
    for (let i = 0; i < 2; i++) {
      const hp = Math.max(0, state.f[i].hp);
      document.getElementById(`hp${i}text`).textContent = hp.toFixed(0);
      const bar = document.getElementById(`hp${i}bar`);
      bar.style.width = hp + '%';
      bar.style.background = hp > 50 ? 'var(--green)' : hp > 25 ? 'var(--yellow)' : 'var(--red)';
    }
  }
  document.getElementById('tickInfo').textContent = `${Math.floor(currentTick)} / ${maxTick}`;
  document.getElementById('scrubber').value = currentTick;
}

function togglePlay() {
  playing = !playing;
  document.getElementById('playBtn').textContent = playing ? 'Pause' : 'Play';
  if (playing) {
    if (currentTick >= maxTick) currentTick = 0;
    lastFrameTime = null;
    requestAnimationFrame(animate);
  }
}

function setSpeed(s) {
  speed = s;
  document.querySelectorAll('.speed-btn').forEach(b => {
    b.classList.toggle('active', parseFloat(b.textContent) === s);
  });
}

async function launchInGodot() {
  const btn = document.getElementById('launchGodotBtn');
  btn.textContent = 'Launching...';
  btn.disabled = true;
  try {
    // Use the actual filename from the loaded battle data, not the semantic match ID
    const filename = (data && data._filename) ? data._filename.replace(/\.json$/, '') : matchId;
    const dirName = await findFighterDir(gen, fighter);
    const res = await fetch('/api/replay/launch', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ tournament, gen: `gen_${String(parseInt(gen)).padStart(3,'0')}`, fighter: dirName, match: filename })
    });
    const result = await res.json();
    if (res.ok) {
      btn.textContent = 'Launched!';
      btn.style.background = '#3fb950';
      setTimeout(() => { btn.textContent = 'Launch in Godot'; btn.disabled = false; }, 3000);
    } else {
      btn.textContent = 'Error';
      btn.style.background = '#f85149';
      console.error(result.error);
      setTimeout(() => { btn.textContent = 'Launch in Godot'; btn.disabled = false; btn.style.background = '#3fb950'; }, 3000);
    }
  } catch (e) {
    btn.textContent = 'Failed';
    btn.style.background = '#f85149';
    console.error(e);
    setTimeout(() => { btn.textContent = 'Launch in Godot'; btn.disabled = false; btn.style.background = '#3fb950'; }, 3000);
  }
}

function scrub(val) {
  currentTick = parseInt(val);
  draw();
}

function animate(now) {
  if (!playing) return;

  // First frame after play: just record the timestamp, don't advance
  if (lastFrameTime === null) {
    lastFrameTime = now;
    draw();
    requestAnimationFrame(animate);
    return;
  }

  const elapsed = (now - lastFrameTime) / 1000; // seconds
  lastFrameTime = now;

  // Clamp elapsed to avoid huge jumps (e.g. tab was backgrounded)
  const dt = Math.min(elapsed, 0.1);

  // Advance by dt * 60 ticks/sec * speed
  currentTick += dt * 60 * speed;
  if (currentTick >= maxTick) {
    currentTick = maxTick;
    playing = false;
    document.getElementById('playBtn').textContent = 'Play';
  }

  draw();
  if (playing) requestAnimationFrame(animate);
}

load();
</script>
</body>
</html>
""";

    private static string BuildBtCompareHtml() => """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>BT Compare — AI-BT-Gym</title>
<style>
  :root { --bg: #0d1117; --card: #161b22; --border: #30363d; --text: #e6edf3; --dim: #8b949e; --accent: #58a6ff; --green: #3fb950; --red: #f85149; --yellow: #d29922; }
  * { margin: 0; padding: 0; box-sizing: border-box; }
  body { background: var(--bg); color: var(--text); font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Helvetica, Arial, sans-serif; font-size: 14px; padding: 24px; }
  .nav { color: var(--dim); margin-bottom: 12px; font-size: 13px; }
  .nav a { color: var(--accent); text-decoration: none; }
  .header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px; }
  h1 { font-size: 24px; font-weight: 600; }
  .subtitle { color: var(--dim); font-size: 13px; margin-top: 2px; }
  .panels { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
  .panel { background: var(--card); border: 1px solid var(--border); border-radius: 8px; padding: 16px; min-height: 400px; }
  .panel-header { margin-bottom: 12px; }
  .panel-title { font-size: 15px; font-weight: 600; margin-bottom: 8px; }
  .selectors { display: flex; gap: 8px; margin-bottom: 12px; }
  select { background: var(--bg); color: var(--text); border: 1px solid var(--border); border-radius: 6px; padding: 6px 10px; font-size: 13px; flex: 1; cursor: pointer; }
  select:focus { outline: none; border-color: var(--accent); }
  .stats-row { display: flex; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; padding: 8px 10px; background: var(--bg); border-radius: 6px; border: 1px solid var(--border); font-size: 12px; }
  .stat { color: var(--dim); }
  .stat b { color: var(--text); margin-left: 2px; }
  .tree-actions { display: flex; gap: 6px; margin-bottom: 10px; }
  .tree-actions button { background: var(--bg); color: var(--dim); border: 1px solid var(--border); border-radius: 4px; padding: 3px 10px; font-size: 11px; cursor: pointer; }
  .tree-actions button:hover { color: var(--text); border-color: var(--accent); }
  .tree-container { font-family: 'SF Mono', 'Fira Code', 'Consolas', monospace; font-size: 13px; line-height: 1.6; }
  .bt-node { padding: 1px 0; }
  .bt-children { padding-left: 20px; border-left: 1px solid var(--border); margin-left: 8px; }
  .bt-children.collapsed { display: none; }
  .node-row { display: flex; align-items: center; gap: 4px; padding: 2px 4px; border-radius: 4px; cursor: default; }
  .node-row:hover { background: rgba(255,255,255,0.04); }
  .toggle { cursor: pointer; width: 16px; text-align: center; font-size: 10px; color: var(--dim); user-select: none; flex-shrink: 0; }
  .toggle:hover { color: var(--text); }
  .badge { display: inline-block; padding: 1px 6px; border-radius: 3px; font-size: 11px; font-weight: 600; letter-spacing: 0.3px; }
  .badge-composite { background: rgba(88,166,255,0.15); color: #58a6ff; }
  .badge-decorator { background: rgba(210,153,34,0.15); color: #d29922; }
  .badge-condition { background: rgba(63,185,80,0.15); color: #3fb950; }
  .badge-action { background: rgba(247,120,186,0.15); color: #f778ba; }
  .node-value { color: var(--dim); margin-left: 4px; font-size: 12px; }
  .node-policy { color: var(--yellow); font-size: 11px; margin-left: 4px; }
  .node-comment { color: var(--dim); margin-left: 6px; font-size: 11px; font-style: italic; opacity: 0.8; }
  .subtree-panel { background: rgba(26,229,229,0.06); border: 1px solid rgba(26,229,229,0.2); border-radius: 6px; padding: 4px 6px; margin: 2px 0; }
  .subtree-label { display: inline-flex; align-items: center; gap: 4px; margin-bottom: 2px; }
  .subtree-badge { background: rgba(26,229,229,0.2); color: #1ae5e5; font-size: 10px; font-weight: 700; padding: 1px 6px; border-radius: 3px; letter-spacing: 0.5px; text-transform: uppercase; }
  .subtree-link { color: #1ae5e5; font-size: 10px; text-decoration: none; opacity: 0.7; cursor: pointer; }
  .subtree-link:hover { opacity: 1; text-decoration: underline; }
  .empty-state { color: var(--dim); text-align: center; padding: 60px 20px; font-size: 13px; }
  .link-btn { background: var(--card); border: 1px solid var(--border); color: var(--accent); padding: 8px 16px; border-radius: 6px; text-decoration: none; font-weight: 600; font-size: 14px; }
</style>
</head>
<body>
<div class="nav"><a href="/" id="dashLink">Tournaments</a> | <strong>BT Compare</strong></div>
<div class="header">
  <div style="display:flex;align-items:center;gap:16px">
    <div><h1>Behavior Tree Compare</h1><p class="subtitle">Side-by-side comparison of fighter behavior trees across generations</p></div>
    <select id="tournamentSelect" onchange="switchTournament(this.value)" style="background:var(--card);color:var(--text);border:1px solid var(--border);border-radius:6px;padding:8px 12px;font-size:14px;cursor:pointer;min-width:160px">
      <option value="default">Loading...</option>
    </select>
  </div>
  <div style="display:flex;gap:8px">
    <a href="/" id="dashLink2" class="link-btn">Dashboard</a>
    <a href="/tests" class="link-btn">Tests</a>
  </div>
</div>

<div class="panels">
  <div class="panel" id="panel-left">
    <div class="panel-header">
      <div class="selectors">
        <select id="gen-left" onchange="onGenChange('left')"><option value="">Generation...</option></select>
        <select id="fighter-left" onchange="onFighterChange('left')"><option value="">Fighter...</option></select>
      </div>
      <div class="stats-row" id="stats-left" style="display:none"></div>
      <div class="tree-actions" id="actions-left" style="display:none">
        <button onclick="expandAll('left')">Expand All</button>
        <button onclick="collapseAll('left')">Collapse All</button>
      </div>
    </div>
    <div class="tree-container" id="tree-left"><div class="empty-state">Select a generation and fighter</div></div>
  </div>
  <div class="panel" id="panel-right">
    <div class="panel-header">
      <div class="selectors">
        <select id="gen-right" onchange="onGenChange('right')"><option value="">Generation...</option></select>
        <select id="fighter-right" onchange="onFighterChange('right')"><option value="">Fighter...</option></select>
      </div>
      <div class="stats-row" id="stats-right" style="display:none"></div>
      <div class="tree-actions" id="actions-right" style="display:none">
        <button onclick="expandAll('right')">Expand All</button>
        <button onclick="collapseAll('right')">Collapse All</button>
      </div>
    </div>
    <div class="tree-container" id="tree-right"><div class="empty-state">Select a generation and fighter</div></div>
  </div>
</div>

<script>
let generations = [];
let fighterCache = {}; // genId -> fighters array
let currentTournament = 'default';

function apiBase() {
  return currentTournament === 'default' ? '/api' : `/api/tournaments/${currentTournament}`;
}

async function loadTournaments() {
  const res = await fetch('/api/tournaments');
  const tournaments = await res.json();
  const sel = document.getElementById('tournamentSelect');
  sel.innerHTML = '';
  tournaments.forEach(t => {
    const opt = document.createElement('option');
    opt.value = t.id;
    opt.textContent = t.display_name + (t.generation_count > 0 ? ` (${t.generation_count} gens)` : '');
    sel.appendChild(opt);
  });
  // Read from URL
  const params = new URLSearchParams(location.search);
  if (params.has('tournament')) currentTournament = params.get('tournament');
  sel.value = currentTournament;
  updateDashLinks();
}

function switchTournament(tid) {
  currentTournament = tid;
  fighterCache = {};
  updateDashLinks();
  loadGenerations();
}

function updateDashLinks() {
  const tq = currentTournament !== 'default' ? `?tournament=${currentTournament}` : '';
  const dl = document.getElementById('dashLink');
  if (dl) dl.href = '/' + tq;
  const dl2 = document.getElementById('dashLink2');
  if (dl2) dl2.href = '/' + tq;
}

async function loadGenerations() {
  const res = await fetch(apiBase() + '/generations');
  generations = await res.json();
  for (const side of ['left', 'right']) {
    const sel = document.getElementById('gen-' + side);
    sel.innerHTML = '<option value="">Generation...</option>';
    generations.forEach((g, i) => {
      const opt = document.createElement('option');
      opt.value = i;
      opt.textContent = 'Gen ' + String(i).padStart(3, '0') + ' (' + g.leaderboard.length + ' fighters)';
      sel.appendChild(opt);
    });
    // Add Sub-Trees pseudo-generation
    const stOpt = document.createElement('option');
    stOpt.value = 'sub-trees';
    stOpt.textContent = 'Sub-Trees (shared patterns)';
    stOpt.style.color = '#1ae5e5';
    sel.appendChild(stOpt);
  }
  applyUrlParams();
}

async function onGenChange(side) {
  const genSel = document.getElementById('gen-' + side);
  const fighterSel = document.getElementById('fighter-' + side);
  fighterSel.innerHTML = '<option value="">Fighter...</option>';
  document.getElementById('tree-' + side).innerHTML = '<div class="empty-state">Select a fighter</div>';
  document.getElementById('stats-' + side).style.display = 'none';
  document.getElementById('actions-' + side).style.display = 'none';

  const genIdx = genSel.value;
  if (genIdx === '') return;

  if (genIdx === 'sub-trees') {
    // Fetch subtree list
    const stRes = await fetch(apiBase() + '/generations/sub-trees/fighters');
    const subtrees = await stRes.json();
    subtrees.forEach(f => {
      const opt = document.createElement('option');
      opt.value = f.fighter_id;
      opt.textContent = f.name;
      opt.style.color = '#1ae5e5';
      fighterSel.appendChild(opt);
    });
    return;
  }

  const gen = generations[parseInt(genIdx)];
  if (!gen) return;

  // Use leaderboard data for fighter list (has name and color)
  gen.leaderboard.forEach(f => {
    const opt = document.createElement('option');
    opt.value = f.fighter_id;
    opt.textContent = f.name;
    opt.style.color = f.color || '';
    fighterSel.appendChild(opt);
  });
}

async function onFighterChange(side) {
  const genIdx = document.getElementById('gen-' + side).value;
  const fighterId = document.getElementById('fighter-' + side).value;
  if (genIdx === '' || fighterId === '') return;

  const container = document.getElementById('tree-' + side);
  container.innerHTML = '<div class="empty-state">Loading...</div>';

  try {
    const res = await fetch(apiBase() + '/generations/' + genIdx + '/fighters/' + fighterId + '/bt');
    if (!res.ok) throw new Error('BT not found');
    const bt = await res.json();
    renderTree(bt, container, side);
    updateUrl();
  } catch (e) {
    container.innerHTML = '<div class="empty-state">Failed to load BT: ' + e.message + '</div>';
  }
}

function renderTree(roots, container, side) {
  container.innerHTML = '';
  const stats = computeStats(roots);
  showStats(stats, side);
  document.getElementById('actions-' + side).style.display = 'flex';

  roots.forEach(root => {
    container.appendChild(buildNodeEl(root));
  });
}

function buildNodeEl(node) {
  const div = document.createElement('div');
  div.className = 'bt-node';

  const row = document.createElement('div');
  row.className = 'node-row';

  const hasChildren = node.children && node.children.length > 0;
  const category = getCategory(node.type);

  // Toggle arrow
  if (hasChildren) {
    const toggle = document.createElement('span');
    toggle.className = 'toggle';
    toggle.textContent = '\u25BC';
    toggle.onclick = function() { toggleNode(this); };
    row.appendChild(toggle);
  } else {
    const spacer = document.createElement('span');
    spacer.className = 'toggle';
    spacer.textContent = '\u00B7';
    spacer.style.color = 'var(--border)';
    row.appendChild(spacer);
  }

  // Badge
  const badge = document.createElement('span');
  badge.className = 'badge badge-' + category;
  badge.textContent = node.type;
  row.appendChild(badge);

  // Value / policy
  if (node.policy) {
    const pol = document.createElement('span');
    pol.className = 'node-policy';
    pol.textContent = node.policy;
    row.appendChild(pol);
  }
  if (node.value) {
    const val = document.createElement('span');
    val.className = 'node-value';
    if (category === 'condition') {
      val.textContent = node.value;
    } else if (category === 'action') {
      val.textContent = node.value;
    } else {
      val.textContent = '(' + node.value + ')';
    }
    row.appendChild(val);
  }
  if (node.comment) {
    const cmt = document.createElement('span');
    cmt.className = 'node-comment';
    cmt.textContent = '// ' + node.comment;
    row.appendChild(cmt);
  }

  div.appendChild(row);

  // Children
  if (hasChildren) {
    const childrenDiv = document.createElement('div');
    childrenDiv.className = 'bt-children';
    node.children.forEach(child => {
      childrenDiv.appendChild(buildNodeEl(child));
    });
    div.appendChild(childrenDiv);
  }

  // Wrap in subtree panel if tagged
  if (node.subTree) {
    const panel = document.createElement('div');
    panel.className = 'subtree-panel';
    const label = document.createElement('div');
    label.className = 'subtree-label';
    const stBadge = document.createElement('span');
    stBadge.className = 'subtree-badge';
    stBadge.textContent = node.subTree;
    label.appendChild(stBadge);
    const stLink = document.createElement('a');
    stLink.className = 'subtree-link';
    stLink.textContent = 'view';
    stLink.href = '/bt-compare?' + (currentTournament !== 'default' ? 'tournament=' + currentTournament + '&' : '') + 'left_gen=sub-trees&left_fighter=' + encodeURIComponent(node.subTree);
    label.appendChild(stLink);
    panel.appendChild(label);
    panel.appendChild(div);
    return panel;
  }

  return div;
}

function getCategory(type) {
  if (['Sequence', 'Selector', 'Parallel'].includes(type)) return 'composite';
  if (['Inverter', 'Repeater', 'Cooldown', 'ConditionGate'].includes(type)) return 'decorator';
  if (type === 'Condition') return 'condition';
  return 'action';
}

function toggleNode(el) {
  const children = el.closest('.bt-node').querySelector('.bt-children');
  if (!children) return;
  const collapsed = children.classList.toggle('collapsed');
  el.textContent = collapsed ? '\u25B6' : '\u25BC';
}

function expandAll(side) {
  document.getElementById('tree-' + side).querySelectorAll('.bt-children').forEach(el => {
    el.classList.remove('collapsed');
  });
  document.getElementById('tree-' + side).querySelectorAll('.toggle').forEach(el => {
    if (el.textContent === '\u25B6') el.textContent = '\u25BC';
  });
}

function collapseAll(side) {
  document.getElementById('tree-' + side).querySelectorAll('.bt-children').forEach(el => {
    el.classList.add('collapsed');
  });
  document.getElementById('tree-' + side).querySelectorAll('.toggle').forEach(el => {
    if (el.textContent === '\u25BC') el.textContent = '\u25B6';
  });
}

function computeStats(roots) {
  let total = 0, composites = 0, decorators = 0, conditions = 0, actions = 0, maxDepth = 0;
  function walk(node, depth) {
    total++;
    if (depth > maxDepth) maxDepth = depth;
    const cat = getCategory(node.type);
    if (cat === 'composite') composites++;
    else if (cat === 'decorator') decorators++;
    else if (cat === 'condition') conditions++;
    else actions++;
    if (node.children) node.children.forEach(c => walk(c, depth + 1));
  }
  roots.forEach(r => walk(r, 0));
  return { total, composites, decorators, conditions, actions, maxDepth };
}

function showStats(stats, side) {
  const el = document.getElementById('stats-' + side);
  el.style.display = 'flex';
  el.innerHTML =
    '<span class="stat">Nodes: <b>' + stats.total + '</b></span>' +
    '<span class="stat">Depth: <b>' + stats.maxDepth + '</b></span>' +
    '<span class="stat" style="color:#58a6ff">Composites: <b>' + stats.composites + '</b></span>' +
    '<span class="stat" style="color:#d29922">Decorators: <b>' + stats.decorators + '</b></span>' +
    '<span class="stat" style="color:#3fb950">Conditions: <b>' + stats.conditions + '</b></span>' +
    '<span class="stat" style="color:#f778ba">Actions: <b>' + stats.actions + '</b></span>';
}

function updateUrl() {
  const params = new URLSearchParams();
  if (currentTournament !== 'default') params.set('tournament', currentTournament);
  const lg = document.getElementById('gen-left').value;
  const lf = document.getElementById('fighter-left').value;
  const rg = document.getElementById('gen-right').value;
  const rf = document.getElementById('fighter-right').value;
  if (lg) params.set('left_gen', lg);
  if (lf) params.set('left_fighter', lf);
  if (rg) params.set('right_gen', rg);
  if (rf) params.set('right_fighter', rf);
  const qs = params.toString();
  history.replaceState(null, '', qs ? '?' + qs : '/bt-compare');
}

async function applyUrlParams() {
  const params = new URLSearchParams(window.location.search);
  for (const side of ['left', 'right']) {
    const genVal = params.get(side + '_gen');
    const fighterVal = params.get(side + '_fighter');
    if (genVal !== null) {
      document.getElementById('gen-' + side).value = genVal;
      await onGenChange(side);
      if (fighterVal) {
        document.getElementById('fighter-' + side).value = fighterVal;
        await onFighterChange(side);
      }
    }
  }
}

loadTournaments().then(() => loadGenerations());
</script>
</body>
</html>
""";

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
