namespace AiBtGym.Godot;

internal static class DashboardHtml
{
    internal static string Build() => """
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
}
