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
using AiBtGym.Tests;

namespace AiBtGym.Godot;

public partial class TournamentRunner : Node
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private string _outputPath = "";
    private bool _tournamentRunning;
    private GenerationSummary? _lastSummary;
    private List<TestResult>? _lastTestResults;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public override void _Ready()
    {
        _outputPath = Path.Combine(
            ProjectSettings.GlobalizePath("res://"), "generations");

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
            else if (path == "/api/tests/run" && method == "POST")
                await RunTests(res);
            else if (path == "/api/tests/results" && method == "GET")
                await ServeJson(res, _lastTestResults ?? new List<TestResult>());
            else if (path == "/api/status" && method == "GET")
                await ServeJson(res, new { tournamentRunning = _tournamentRunning, lastSummary = _lastSummary });
            else if (path == "/api/tournament/run" && method == "POST")
                await RunTournament(req, res);
            else if (path == "/api/generations" && method == "GET")
                await ServeGenerations(res);
            else if (path == "/bt-compare" && method == "GET")
                await ServeBtCompare(res);
            else if (path.StartsWith("/api/generations/") && method == "GET")
                await ServeGenerationData(res, path);
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

    private async System.Threading.Tasks.Task RunTournament(HttpListenerRequest req, HttpListenerResponse res)
    {
        if (_tournamentRunning)
        {
            res.StatusCode = 409;
            await ServeJson(res, new { error = "Tournament already running" });
            return;
        }

        // Determine generation number
        int gen = 0;
        if (Directory.Exists(_outputPath))
        {
            var dirs = Directory.GetDirectories(_outputPath, "gen_*");
            if (dirs.Length > 0)
                gen = dirs.Select(d => int.TryParse(Path.GetFileName(d).Replace("gen_", ""), out int n) ? n : -1).Max() + 1;
        }

        _tournamentRunning = true;
        GD.Print($"Starting generation {gen}...");

        try
        {
            // Select trees based on generation
            var (names, trees, hexColors) = gen switch
            {
                >= 3 => (Gen003Trees.Names, Gen003Trees.All, Gen003Trees.HexColors),
                2    => (Gen002Trees.Names, Gen002Trees.All, Gen002Trees.HexColors),
                1    => (Gen001Trees.Names, Gen001Trees.All, Gen001Trees.HexColors),
                _    => (SeedTrees.Names, SeedTrees.All, SeedTrees.HexColors)
            };
            var entries = Tournament.EntriesFromSeed(names, trees, hexColors);
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var summary = Tournament.RunGeneration(entries, generation: gen, outputPath: _outputPath);
            stopwatch.Stop();

            _lastSummary = summary;
            _tournamentRunning = false;

            GD.Print($"  Gen {gen} completed in {stopwatch.ElapsedMilliseconds}ms");
            foreach (var entry in summary.Leaderboard)
                GD.Print($"    #{entry.Rank}  {entry.Name,-28} ELO {entry.Elo,7:F1}  {entry.Record}");

            await ServeJson(res, new
            {
                generation = gen,
                elapsedMs = stopwatch.ElapsedMilliseconds,
                summary
            });
        }
        catch (Exception ex)
        {
            _tournamentRunning = false;
            res.StatusCode = 500;
            await ServeJson(res, new { error = ex.Message });
        }
    }

    private async System.Threading.Tasks.Task ServeGenerations(HttpListenerResponse res)
    {
        var gens = new List<object>();
        if (Directory.Exists(_outputPath))
        {
            foreach (var dir in Directory.GetDirectories(_outputPath, "gen_*").OrderBy(d => d))
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

    private async System.Threading.Tasks.Task ServeGenerationData(HttpListenerResponse res, string path)
    {
        // /api/generations/{id} — generation summary
        // /api/generations/{id}/fighters — list fighters
        // /api/generations/{id}/fighters/{fid} — fighter status
        // /api/generations/{id}/fighters/{fid}/battles — list battles
        // /api/generations/{id}/fighters/{fid}/battles/{bid} — battle log
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // parts: ["api", "generations", "{id}", ...]

        if (parts.Length < 3) { res.StatusCode = 400; await WriteText(res, "Bad request"); return; }

        // Handle "sub-trees" pseudo-generation
        if (parts[2] == "sub-trees")
        {
            await ServeSubTreeData(res, parts);
            return;
        }

        string genDir = Path.Combine(_outputPath, $"gen_{int.Parse(parts[2]):D3}");
        if (!Directory.Exists(genDir)) { res.StatusCode = 404; await WriteText(res, "Generation not found"); return; }

        if (parts.Length == 3)
        {
            // Generation summary
            var summaryPath = Path.Combine(genDir, "generation_summary.json");
            if (File.Exists(summaryPath))
                await ServeRawJson(res, File.ReadAllText(summaryPath));
            else
            { res.StatusCode = 404; await WriteText(res, "Summary not found"); }
            return;
        }

        if (parts.Length >= 4 && parts[3] == "fighters")
        {
            var fightersDir = Path.Combine(genDir, "fighters");
            if (!Directory.Exists(fightersDir)) { res.StatusCode = 404; await WriteText(res, "No fighters"); return; }

            if (parts.Length == 4)
            {
                // List fighters
                var fighters = new List<object>();
                foreach (var dir in Directory.GetDirectories(fightersDir).OrderBy(d => d))
                {
                    var statusPath = Path.Combine(dir, "status.json");
                    if (File.Exists(statusPath))
                    {
                        var json = File.ReadAllText(statusPath);
                        var status = JsonSerializer.Deserialize<FighterStatus>(json, TournamentJson.Options);
                        if (status != null) fighters.Add(status);
                    }
                }
                await ServeJson(res, fighters);
                return;
            }

            // Find fighter dir by partial match
            string fighterId = parts[4];
            var fighterDir = Directory.GetDirectories(fightersDir)
                .FirstOrDefault(d => Path.GetFileName(d).StartsWith(fighterId));
            if (fighterDir == null) { res.StatusCode = 404; await WriteText(res, "Fighter not found"); return; }

            if (parts.Length == 5)
            {
                // Fighter status
                var statusPath = Path.Combine(fighterDir, "status.json");
                if (File.Exists(statusPath))
                    await ServeRawJson(res, File.ReadAllText(statusPath));
                else
                { res.StatusCode = 404; await WriteText(res, "Status not found"); }
                return;
            }

            if (parts.Length >= 6 && parts[5] == "bt")
            {
                var btPath = Path.Combine(fighterDir, "bt_v0.json");
                if (File.Exists(btPath))
                    await ServeRawJson(res, File.ReadAllText(btPath));
                else
                { res.StatusCode = 404; await WriteText(res, "BT not found"); }
                return;
            }

            if (parts.Length >= 6 && parts[5] == "battles")
            {
                var battlesDir = Path.Combine(fighterDir, "battles");
                if (!Directory.Exists(battlesDir)) { res.StatusCode = 404; await WriteText(res, "No battles"); return; }

                if (parts.Length == 6)
                {
                    // List battles — include filename for each so the client can reference the path
                    var battles = new List<object>();
                    foreach (var file in Directory.GetFiles(battlesDir, "*.json").OrderBy(f => f))
                    {
                        var json = File.ReadAllText(file);
                        var log = JsonSerializer.Deserialize<BattleLog>(json, TournamentJson.Options);
                        if (log != null)
                        {
                            // Re-serialize with the filename injected
                            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json, TournamentJson.Options)!;
                            dict["_filename"] = Path.GetFileName(file);
                            battles.Add(dict);
                        }
                    }
                    await ServeJson(res, battles);
                    return;
                }

                if (parts.Length == 7)
                {
                    // Single battle log
                    string battleFile = parts[6];
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
        stopwatch.Stop();

        int passed = _lastTestResults.Count(r => r.Passed);
        int failed = _lastTestResults.Count(r => !r.Passed);
        GD.Print($"Tests: {passed} passed, {failed} failed in {stopwatch.ElapsedMilliseconds}ms");

        await ServeJson(res, new { elapsed_ms = stopwatch.ElapsedMilliseconds, passed, failed, results = _lastTestResults });
    }

    private async System.Threading.Tasks.Task ServeTestPage(HttpListenerResponse res)
    {
        res.ContentType = "text/html; charset=utf-8";
        await WriteBytes(res, Encoding.UTF8.GetBytes(BuildTestPageHtml()));
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
  <div><h1>AI-BT-Gym</h1><p class="subtitle">Evolutionary Behavior Tree Tournament</p></div>
  <div style="display:flex;gap:8px">
    <a href="/bt-compare" style="background:var(--card);border:1px solid var(--border);color:var(--accent);padding:8px 16px;border-radius:6px;text-decoration:none;font-weight:600;font-size:14px">BT Compare</a>
    <a href="/tests" style="background:var(--card);border:1px solid var(--border);color:var(--accent);padding:8px 16px;border-radius:6px;text-decoration:none;font-weight:600;font-size:14px">Tests</a>
  </div>
</div>
<div id="breadcrumb" class="breadcrumb"></div>
<div id="content"></div>

<script>
const API = '';
let genCache = {}; // gen number -> {timestamp, ...}

// Navigation state
let nav = { view: 'home' };

async function api(path) {
  const r = await fetch(API + path);
  return r.json();
}

function fmtTimestamp(ts) {
  if (!ts) return '';
  const d = new Date(ts);
  const pad = n => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth()+1)}-${pad(d.getDate())}_${pad(d.getHours())}-${pad(d.getMinutes())}`;
}

function getRelativePath() {
  const pad = n => String(n).padStart(3, '0');
  if (nav.view === 'home') return 'generations';
  if (nav.view === 'gen') return `generations/gen_${pad(nav.gen)}`;
  if (nav.view === 'fighter') return `generations/gen_${pad(nav.gen)}/fighters/${nav.fighterId}_${nav.fighterName}`;
  if (nav.view === 'battle') return `generations/gen_${pad(nav.gen)}/fighters/${nav.fighterId}_${nav.fighterName}/battles/${nav.matchFile || ''}`;
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

async function showHome() {
  nav = { view: 'home' };
  renderBreadcrumb();

  const gens = await api('/api/generations');
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
    const r = await fetch(API + '/api/tournament/run', { method: 'POST' });
    const data = await r.json();
    if (data.error) { status.textContent = 'Error: ' + data.error; }
    else { status.textContent = `Gen ${data.generation} completed in ${data.elapsed_ms}ms`; }
    showHome();
  } catch(e) { status.textContent = 'Failed: ' + e.message; }
  btn.disabled = false;
}

async function showGeneration(gen) {
  const genData = await api(`/api/generations/${gen}`);
  nav = { view: 'gen', gen, genTimestamp: genData.timestamp || genCache[gen] };
  genCache[gen] = nav.genTimestamp;
  renderBreadcrumb();

  const fighters = await api(`/api/generations/${gen}/fighters`);
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
        <span class="metric"><span>Avg Dmg Dealt:</span> ${f.aggregate_metrics.avg_damage_dealt.toFixed(1)}</span>
        <span class="metric"><span>Avg Dmg Taken:</span> ${f.aggregate_metrics.avg_damage_received.toFixed(1)}</span>
        <span class="metric"><span>Hit Accuracy:</span> ${(f.aggregate_metrics.avg_hit_accuracy*100).toFixed(0)}%</span>
        <span class="metric"><span>KOs:</span> ${f.aggregate_metrics.total_knockouts}</span>
      </div>
    </div>
  `).join('');
}

async function showFighter(gen, fighterId, fighterName) {
  if (!fighterName) {
    const fighters = await api(`/api/generations/${gen}/fighters`);
    const f = fighters.find(f => f.fighter_id === fighterId);
    fighterName = f ? f.name : fighterId;
  }
  nav = { ...nav, view: 'fighter', gen, fighterId, fighterName };
  if (!nav.genTimestamp) nav.genTimestamp = genCache[gen];
  renderBreadcrumb();

  const battles = await api(`/api/generations/${gen}/fighters/${fighterId}/battles`);
  const el = document.getElementById('content');
  el.innerHTML = battles.map((b, i) => `
    <div class="card clickable" onclick='showBattle(${JSON.stringify(b).replace(/'/g, "\\u0027")}, "${fighterId}")'>
      <div style="display:flex;justify-content:space-between;align-items:center">
        <h2>vs ${b.opponent}</h2>
        <div>
          <a href="/replay?gen=${gen}&fighter=${fighterId}&match=${b.match_id}" target="_blank" onclick="event.stopPropagation()" style="background:var(--accent);color:#fff;padding:4px 12px;border-radius:4px;text-decoration:none;font-size:13px;margin-right:8px">Watch</a>
          <span class="${b.result}" style="font-weight:700;font-size:18px">${b.result.toUpperCase()}</span>
        </div>
      </div>
      <div>
        <span class="metric"><span>Duration:</span> ${b.duration_seconds.toFixed(1)}s</span>
        <span class="metric"><span>HP:</span> ${b.final_state.fighter_hp.toFixed(0)} vs ${b.final_state.opponent_hp.toFixed(0)}</span>
        <span class="metric"><span>Hits:</span> ${b.damage_summary.hits_landed} landed / ${b.damage_summary.hits_taken} taken</span>
        <span class="metric"><span>Accuracy:</span> ${(b.damage_summary.hit_accuracy*100).toFixed(0)}%</span>
      </div>
    </div>
  `).join('');
}

function showBattle(b, fighterId) {
  if (fighterId) nav.fighterId = fighterId;
  nav = { ...nav, view: 'battle', opponentName: b.opponent, matchId: b.match_id, matchFile: b._filename || '' };
  renderBreadcrumb();

  const el = document.getElementById('content');
  const actions = Object.entries(b.action_frequency || {}).sort((a,b) => b[1]-a[1]);
  const totalActions = actions.reduce((s,[,v]) => s+v, 0) || 1;

  el.innerHTML = `
    <div style="margin-bottom:12px">
      <a href="/replay?gen=${nav.gen}&fighter=${nav.fighterId}&match=${b.match_id}" target="_blank" style="background:var(--accent);color:#fff;padding:6px 16px;border-radius:4px;text-decoration:none;font-size:14px;font-weight:600">Watch Replay</a>
      <span style="margin-left:12px;font-size:18px" class="${b.result}"><strong>${b.result.toUpperCase()}</strong></span>
      <span class="subtitle" style="margin-left:8px">${b.duration_seconds.toFixed(1)}s</span>
    </div>

    <div class="card">
      <h2>Final State</h2>
      <div>
        <span class="metric"><span>Fighter HP:</span> ${b.final_state.fighter_hp.toFixed(0)}/100</span>
        <span class="metric"><span>Opponent HP:</span> ${b.final_state.opponent_hp.toFixed(0)}/100</span>
        <span class="metric"><span>Dmg Dealt:</span> ${b.damage_summary.dealt.toFixed(0)}</span>
        <span class="metric"><span>Dmg Received:</span> ${b.damage_summary.received.toFixed(0)}</span>
      </div>
    </div>

    <div class="card">
      <h2>Positional Summary</h2>
      <div>
        <span class="metric"><span>Grounded:</span> ${(b.positional_summary.time_grounded_pct*100).toFixed(0)}%</span>
        <span class="metric"><span>Airborne:</span> ${(b.positional_summary.time_airborne_pct*100).toFixed(0)}%</span>
        <span class="metric"><span>Near Opponent:</span> ${(b.positional_summary.time_near_opponent_pct*100).toFixed(0)}%</span>
        <span class="metric"><span>Avg Distance:</span> ${b.positional_summary.avg_distance_to_opponent.toFixed(0)}px</span>
      </div>
    </div>

    <div class="card">
      <h2>Grapple Stats</h2>
      <div>
        <span class="metric"><span>Attaches:</span> ${b.grapple_stats.attach_count}</span>
        <span class="metric"><span>Ceiling:</span> ${b.grapple_stats.ceiling_attaches}</span>
        <span class="metric"><span>Wall:</span> ${b.grapple_stats.wall_attaches}</span>
        <span class="metric"><span>Avg Duration:</span> ${b.grapple_stats.avg_attached_duration_ticks.toFixed(0)} ticks</span>
      </div>
    </div>

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

    <div class="card">
      <h2>Phase Breakdown</h2>
      <div class="phase-grid">
        ${(b.phase_breakdown||[]).map(p => `
          <div class="phase-card">
            <h4>${p.phase} (tick ${p.tick_range[0]}–${p.tick_range[1]})</h4>
            <div><span class="metric win"><span>Dealt:</span> ${p.damage_dealt.toFixed(0)}</span></div>
            <div><span class="metric loss"><span>Received:</span> ${p.damage_received.toFixed(0)}</span></div>
            <div style="font-size:12px;color:var(--dim);margin-top:4px">HP: ${p.hp_at_end[0].toFixed(0)} vs ${p.hp_at_end[1].toFixed(0)}</div>
          </div>
        `).join('')}
      </div>
    </div>

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

    <div class="card">
      <h2>Hit Log (${(b.hit_log||[]).length} hits)</h2>
      <div class="hit-timeline">
        ${(b.hit_log||[]).map(h => `
          <div class="hit-row">
            <span><span class="${h.attacker==='fighter'?'win':'loss'}" style="font-weight:600">${h.attacker}</span> ${h.hand} fist</span>
            <span style="color:var(--dim)">tick ${h.tick} — ${h.damage} dmg</span>
          </div>
        `).join('')}
      </div>
    </div>
  `;
}

showHome();
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
  .nav { margin-bottom: 12px; font-size: 13px; }
  .nav a { color: var(--accent); text-decoration: none; } .nav a:hover { text-decoration: underline; }
</style>
</head>
<body>
<div class="nav"><a href="/">Tournaments</a> | <strong>Tests</strong></div>
<div class="header">
  <div><h1>Movement Tests</h1><p class="subtitle">End-to-end physics and behavior tests with visual replay</p></div>
  <div style="display:flex;align-items:center"><button id="runBtn" onclick="runTests()">Run Tests</button><span id="statusText" class="status"></span></div>
</div>
<div id="results"></div>

<script>
let testData = null;
let activeReplay = null; // {idx, playing, speed, currentTick, maxTick, lastFrameTime, canvas, ctx}

async function runTests() {
  const btn = document.getElementById('runBtn');
  const status = document.getElementById('statusText');
  btn.disabled = true;
  status.textContent = 'Running...';
  try {
    const r = await fetch('/api/tests/run', { method: 'POST' });
    testData = await r.json();
    status.textContent = `${testData.passed} passed, ${testData.failed} failed in ${testData.elapsed_ms}ms`;
    renderResults();
  } catch(e) { status.textContent = 'Error: ' + e.message; }
  btn.disabled = false;
}

async function loadCached() {
  try {
    const r = await fetch('/api/tests/results');
    const results = await r.json();
    if (results.length > 0) {
      const passed = results.filter(r => r.passed).length;
      const failed = results.filter(r => !r.passed).length;
      testData = { passed, failed, results };
      document.getElementById('statusText').textContent = `${passed} passed, ${failed} failed (cached)`;
      renderResults();
    }
  } catch(e) {}
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
</style>
</head>
<body>
<div id="breadcrumb" class="breadcrumb"></div>
<p class="subtitle" id="matchInfo">Loading...</p>
<canvas id="arena" width="1500" height="680"></canvas>
<div class="controls">
  <button id="playBtn" onclick="togglePlay()">Play</button>
  <button class="speed-btn" onclick="setSpeed(0.5)">0.5x</button>
  <button class="speed-btn active" onclick="setSpeed(1)">1x</button>
  <button class="speed-btn" onclick="setSpeed(2)">2x</button>
  <button class="speed-btn" onclick="setSpeed(4)">4x</button>
  <input type="range" id="scrubber" min="0" max="100" value="0" oninput="scrub(this.value)">
  <span class="info" id="tickInfo">0 / 0</span>
</div>
<div class="hud">
  <div class="hud-fighter">
    <div><span style="color:#f85149;font-weight:700" id="name0">Fighter 0</span> <span id="hp0text">100</span> HP</div>
    <div class="hp-bar"><div class="hp-fill" id="hp0bar" style="width:100%;background:var(--green)"></div></div>
  </div>
  <div class="hud-fighter" style="text-align:right">
    <div><span style="color:#58a6ff;font-weight:700" id="name1">Fighter 1</span> <span id="hp1text">100</span> HP</div>
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

let data = null; // full battle log with replay
let replay = null;
let playing = false;
let speed = 1;
let currentTick = 0;
let maxTick = 0;
let lastFrameTime = null;
let hitFlashes = []; // [{tick, x, y, ttl}]

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
    const fighters = await (await fetch(`/api/generations/${gen}/fighters`)).json();
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
    const battles = await (await fetch(`/api/generations/${gen}/fighters/${fighter}/battles`)).json();
    const battle = battles.find(b => b.match_id === matchId);
    if (!battle) { document.getElementById('error').textContent = 'Battle not found'; return; }
    if (!battle.replay) { document.getElementById('error').textContent = 'No replay data in this battle log (re-run tournament)'; return; }

    data = battle;
    replay = battle.replay;
    maxTick = battle.duration_ticks;

    // Apply fighter colors from battle log
    if (battle.fighter_color) F0_COLOR = battle.fighter_color;
    if (battle.opponent_color) F1_COLOR = battle.opponent_color;

    document.getElementById('matchInfo').textContent = `${battle.fighter} vs ${battle.opponent} — ${battle.result.toUpperCase()} in ${battle.duration_seconds.toFixed(1)}s`;

    // Build relative file path for copy button
    const pad = n => String(n).padStart(3, '0');
    const fighterDir = await findFighterDir(gen, fighter);
    const filename = battle._filename || '';
    const relPath = filename
      ? `generations/gen_${pad(parseInt(gen))}/fighters/${fighterDir}/battles/${filename}`
      : `generations/gen_${pad(parseInt(gen))}/fighters/${fighterDir}/battles`;

    // Breadcrumb
    const bc = document.getElementById('breadcrumb');
    bc.innerHTML = `<a href="/">Tournaments</a><span class="sep">&gt;</span>`
      + `<a href="/#gen=${gen}">Gen_${gen}</a><span class="sep">&gt;</span>`
      + `<a href="/#gen=${gen}&fighter=${fighter}">${battle.fighter}</a><span class="sep">&gt;</span>`
      + `<span>vs ${battle.opponent}</span><span class="sep">&gt;</span>`
      + `<span class="current">Replay</span>`
      + `<button class="copy-path" title="${relPath}" onclick="(() => { navigator.clipboard.writeText('${relPath}'); this.textContent='copied!'; setTimeout(() => this.textContent='${relPath}', 1200); })()">${relPath}</button>`;
    document.getElementById('name0').textContent = battle.fighter;
    document.getElementById('name0').style.color = F0_COLOR;
    document.getElementById('name1').textContent = battle.opponent;
    document.getElementById('name1').style.color = F1_COLOR;
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

  if (lo >= cpLen - 1) return replay.checkpoints[cpLen - 1];
  if (hi >= cpLen) return replay.checkpoints[cpLen - 1];

  const a = replay.checkpoints[lo];
  const b = replay.checkpoints[hi];
  const frac = idx - lo;

  // Interpolate
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

function lerp(a, b, t) { return a + (b - a) * t; }

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

  // Draw chains and fists
  const fighterColors = [F0_COLOR, F1_COLOR];
  for (let fi = 0; fi < 4; fi++) {
    const fist = state.fists[fi];
    const ownerIdx = fi < 2 ? 0 : 1;
    const owner = state.f[ownerIdx];

    if (fist.s === 0) continue; // retracted, skip

    // Chain line
    ctx.strokeStyle = CHAIN_COLORS[fist.s] || '#666';
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.moveTo(owner.x, owner.y);
    ctx.lineTo(fist.x, fist.y);
    ctx.stroke();

    // Anchor point (if attached)
    if (fist.a) {
      ctx.fillStyle = ANCHOR_COLOR;
      ctx.beginPath();
      // Diamond shape
      const ax = fist.ax, ay = fist.ay, sz = 5;
      ctx.moveTo(ax, ay - sz);
      ctx.lineTo(ax + sz, ay);
      ctx.lineTo(ax, ay + sz);
      ctx.lineTo(ax - sz, ay);
      ctx.closePath();
      ctx.fill();
    }

    // Fist circle
    ctx.fillStyle = FIST_COLORS[fist.s] || '#888';
    ctx.beginPath();
    ctx.arc(fist.x, fist.y, 8, 0, Math.PI * 2);
    ctx.fill();
    ctx.strokeStyle = fighterColors[ownerIdx];
    ctx.lineWidth = 1;
    ctx.stroke();
  }

  // Draw fighters
  for (let i = 0; i < 2; i++) {
    const f = state.f[i];
    // Body
    ctx.fillStyle = fighterColors[i];
    ctx.globalAlpha = 0.3;
    ctx.beginPath();
    ctx.arc(f.x, f.y, 18, 0, Math.PI * 2);
    ctx.fill();
    ctx.globalAlpha = 1;
    ctx.strokeStyle = fighterColors[i];
    ctx.lineWidth = 2;
    ctx.stroke();

    // Health bar above
    const barW = 36, barH = 4, barY = f.y - 28;
    ctx.fillStyle = '#333';
    ctx.fillRect(f.x - barW/2, barY, barW, barH);
    const hpPct = Math.max(0, f.hp / 100);
    const hpColor = hpPct > 0.5 ? '#3fb950' : hpPct > 0.25 ? '#d29922' : '#f85149';
    ctx.fillStyle = hpColor;
    ctx.fillRect(f.x - barW/2, barY, barW * hpPct, barH);
  }

  // Hit flashes
  const activeHits = (data.hit_log || []).filter(h => Math.abs(h.tick - currentTick) < 8);
  for (const h of activeHits) {
    const age = Math.abs(currentTick - h.tick);
    const alpha = 1 - age / 8;
    const pos = h.attacker === 'fighter' ? h.opponent_pos : h.fighter_pos;
    ctx.fillStyle = `rgba(255, 255, 100, ${alpha * 0.6})`;
    ctx.beginPath();
    ctx.arc(pos[0], pos[1], 24 - age * 2, 0, Math.PI * 2);
    ctx.fill();
  }

  // Tick counter on canvas
  ctx.fillStyle = '#8b949e';
  ctx.font = `${12/vs}px monospace`;
  ctx.fillText(`Tick ${Math.floor(currentTick)} / ${maxTick}`, 20, h - 8);

  ctx.restore();

  // Update HUD
  for (let i = 0; i < 2; i++) {
    const hp = Math.max(0, state.f[i].hp);
    document.getElementById(`hp${i}text`).textContent = hp.toFixed(0);
    const bar = document.getElementById(`hp${i}bar`);
    bar.style.width = hp + '%';
    bar.style.background = hp > 50 ? 'var(--green)' : hp > 25 ? 'var(--yellow)' : 'var(--red)';
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
<div class="nav"><a href="/">Tournaments</a> | <strong>BT Compare</strong></div>
<div class="header">
  <div><h1>Behavior Tree Compare</h1><p class="subtitle">Side-by-side comparison of fighter behavior trees across generations</p></div>
  <div style="display:flex;gap:8px">
    <a href="/" class="link-btn">Dashboard</a>
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

async function loadGenerations() {
  const res = await fetch('/api/generations');
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
    const stRes = await fetch('/api/generations/sub-trees/fighters');
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
    const res = await fetch('/api/generations/' + genIdx + '/fighters/' + fighterId + '/bt');
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
    stLink.href = '/bt-compare?left_gen=sub-trees&left_fighter=' + encodeURIComponent(node.subTree);
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

loadGenerations();
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
