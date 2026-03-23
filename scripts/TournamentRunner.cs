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
using AiBtGym.Simulation;

namespace AiBtGym.Godot;

public partial class TournamentRunner : Node
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private string _outputPath = "";
    private bool _tournamentRunning;
    private GenerationSummary? _lastSummary;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public override void _Ready()
    {
        _outputPath = Path.Combine(
            ProjectSettings.GlobalizePath("res://"), "generations");

        _listener = new HttpListener();
        _listener.Prefixes.Add("http://localhost:8585/");
        _cts = new CancellationTokenSource();

        try
        {
            _listener.Start();
            GD.Print("═══════════════════════════════════════");
            GD.Print("  Tournament Server");
            GD.Print("  http://localhost:8585");
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
            else if (path == "/api/status" && method == "GET")
                await ServeJson(res, new { tournamentRunning = _tournamentRunning, lastSummary = _lastSummary });
            else if (path == "/api/tournament/run" && method == "POST")
                await RunTournament(req, res);
            else if (path == "/api/generations" && method == "GET")
                await ServeGenerations(res);
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
            res.StatusCode = 500;
            await WriteText(res, $"Error: {ex.Message}");
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
            var entries = Tournament.EntriesFromSeed(SeedTrees.Names, SeedTrees.All);
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

            if (parts.Length >= 6 && parts[5] == "battles")
            {
                var battlesDir = Path.Combine(fighterDir, "battles");
                if (!Directory.Exists(battlesDir)) { res.StatusCode = 404; await WriteText(res, "No battles"); return; }

                if (parts.Length == 6)
                {
                    // List battles
                    var battles = new List<object>();
                    foreach (var file in Directory.GetFiles(battlesDir, "*.json").OrderBy(f => f))
                    {
                        var json = File.ReadAllText(file);
                        var log = JsonSerializer.Deserialize<BattleLog>(json, TournamentJson.Options);
                        if (log != null) battles.Add(log);
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

    // ── Dashboard HTML ──

    private async System.Threading.Tasks.Task ServeDashboard(HttpListenerResponse res)
    {
        res.ContentType = "text/html; charset=utf-8";
        var html = BuildDashboardHtml();
        var bytes = Encoding.UTF8.GetBytes(html);
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
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
  .back-btn { background: var(--border); margin-bottom: 12px; font-size: 13px; padding: 6px 14px; }
  #detail { display: none; }
  .bar { height: 6px; border-radius: 3px; background: var(--border); overflow: hidden; }
  .bar-fill { height: 100%; border-radius: 3px; }
  .phase-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; }
  .phase-card { background: var(--bg); padding: 12px; border-radius: 6px; }
  .phase-card h4 { color: var(--dim); text-transform: uppercase; font-size: 12px; margin-bottom: 8px; }
  .hit-timeline { max-height: 300px; overflow-y: auto; font-size: 13px; }
  .hit-row { padding: 4px 0; border-bottom: 1px solid var(--border); display: flex; justify-content: space-between; }
  .moment { padding: 6px 0; border-bottom: 1px solid var(--border); }
  .moment .event-type { color: var(--accent); font-weight: 600; font-size: 12px; text-transform: uppercase; }
  .tabs { display: flex; gap: 4px; margin-bottom: 12px; }
  .tab { background: var(--bg); border: 1px solid var(--border); color: var(--dim); padding: 6px 14px; border-radius: 4px; cursor: pointer; font-size: 13px; }
  .tab.active { background: var(--accent); color: #fff; border-color: var(--accent); }
</style>
</head>
<body>
<h1>AI-BT-Gym</h1>
<p class="subtitle">Evolutionary Behavior Tree Tournament</p>

<div id="main">
  <div class="controls">
    <button id="runBtn" onclick="runTournament()">Run Tournament</button>
    <span id="statusText" class="status"></span>
  </div>
  <div id="generations"></div>
</div>

<div id="detail">
  <button class="back-btn" onclick="showMain()">Back to Generations</button>
  <div id="detailContent"></div>
</div>

<script>
const API = '';
let currentGen = null;

async function api(path) {
  const r = await fetch(API + path);
  return r.json();
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
    loadGenerations();
  } catch(e) { status.textContent = 'Failed: ' + e.message; }
  btn.disabled = false;
}

async function loadGenerations() {
  const gens = await api('/api/generations');
  const el = document.getElementById('generations');
  if (gens.length === 0) {
    el.innerHTML = '<div class="card"><p style="color:var(--dim)">No generations yet. Click "Run Tournament" to start.</p></div>';
    return;
  }
  el.innerHTML = gens.map(g => `
    <div class="card clickable" onclick="showGeneration(${g.generation})">
      <h2>Generation ${g.generation}</h2>
      <div style="margin-bottom:8px">
        <span class="metric"><span>Fighters:</span> ${g.fighter_count}</span>
        <span class="metric"><span>Matches:</span> ${g.total_matches}</span>
        <span class="metric"><span>KO Rate:</span> ${(g.meta_stats.knockout_rate*100).toFixed(0)}%</span>
        <span class="metric"><span>Avg Duration:</span> ${(g.meta_stats.avg_match_duration_ticks/60).toFixed(1)}s</span>
      </div>
      <table>
        <tr><th>#</th><th>Fighter</th><th>ELO</th><th>Record</th></tr>
        ${g.leaderboard.map(e => `
          <tr>
            <td>${e.rank}</td>
            <td>${e.name}</td>
            <td class="elo">${e.elo.toFixed(1)}</td>
            <td class="record">${e.record}</td>
          </tr>
        `).join('')}
      </table>
    </div>
  `).join('');
}

async function showGeneration(gen) {
  currentGen = gen;
  document.getElementById('main').style.display = 'none';
  document.getElementById('detail').style.display = 'block';
  const fighters = await api(`/api/generations/${gen}/fighters`);
  const el = document.getElementById('detailContent');
  el.innerHTML = `<h2 style="color:var(--accent);margin-bottom:16px">Generation ${gen} — Fighters</h2>` +
    fighters.map(f => `
      <div class="card clickable" onclick="showFighter(${gen}, '${f.fighter_id}')">
        <h2>${f.name}</h2>
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

async function showFighter(gen, fighterId) {
  const battles = await api(`/api/generations/${gen}/fighters/${fighterId}/battles`);
  const el = document.getElementById('detailContent');
  el.innerHTML = `
    <button class="back-btn" onclick="showGeneration(${gen})">Back to Fighters</button>
    <h2 style="color:var(--accent);margin-bottom:16px">${fighterId} — Battle Logs</h2>
    ${battles.map((b, i) => `
      <div class="card clickable" onclick='showBattle(${JSON.stringify(b).replace(/'/g, "\\u0027")})'>
        <div style="display:flex;justify-content:space-between;align-items:center">
          <h2>vs ${b.opponent}</h2>
          <span class="${b.result}" style="font-weight:700;font-size:18px">${b.result.toUpperCase()}</span>
        </div>
        <div>
          <span class="metric"><span>Duration:</span> ${b.duration_seconds.toFixed(1)}s</span>
          <span class="metric"><span>HP:</span> ${b.final_state.fighter_hp.toFixed(0)} vs ${b.final_state.opponent_hp.toFixed(0)}</span>
          <span class="metric"><span>Hits:</span> ${b.damage_summary.hits_landed} landed / ${b.damage_summary.hits_taken} taken</span>
          <span class="metric"><span>Accuracy:</span> ${(b.damage_summary.hit_accuracy*100).toFixed(0)}%</span>
        </div>
      </div>
    `).join('')}
  `;
}

function showBattle(b) {
  const el = document.getElementById('detailContent');
  const actions = Object.entries(b.action_frequency || {}).sort((a,b) => b[1]-a[1]);
  const totalActions = actions.reduce((s,[,v]) => s+v, 0) || 1;

  el.innerHTML = `
    <button class="back-btn" onclick="showFighter(${currentGen}, '${b.match_id.split('_match')[0].replace('gen_'+String(currentGen).padStart(3,'0')+'_','')}')">Back</button>
    <button class="back-btn" onclick="showGeneration(${currentGen})">Back to Fighters</button>
    <h2 style="color:var(--accent);margin-bottom:4px">${b.fighter} vs ${b.opponent}</h2>
    <p class="subtitle">${b.match_id} — <span class="${b.result}" style="font-weight:700">${b.result.toUpperCase()}</span> in ${b.duration_seconds.toFixed(1)}s</p>

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

function showMain() {
  document.getElementById('main').style.display = 'block';
  document.getElementById('detail').style.display = 'none';
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
        var bytes = Encoding.UTF8.GetBytes(json);
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    private static async System.Threading.Tasks.Task ServeRawJson(HttpListenerResponse res, string json)
    {
        res.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(json);
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }

    private static async System.Threading.Tasks.Task WriteText(HttpListenerResponse res, string text)
    {
        res.ContentType = "text/plain; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(text);
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }
}
