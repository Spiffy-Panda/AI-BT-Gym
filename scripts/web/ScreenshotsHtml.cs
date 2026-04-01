namespace AiBtGym.Godot;

internal static class ScreenshotsHtml
{
    internal static string Build() => """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Screenshots — AI-BT-Gym</title>
<style>
  :root { --bg: #0d1117; --card: #161b22; --border: #30363d; --text: #e6edf3; --dim: #8b949e; --accent: #58a6ff; --green: #3fb950; --red: #f85149; }
  * { margin: 0; padding: 0; box-sizing: border-box; }
  body { font-family: 'Segoe UI', system-ui, sans-serif; background: var(--bg); color: var(--text); padding: 20px; }
  h1 { color: var(--accent); margin-bottom: 4px; }
  .subtitle { color: var(--dim); margin-bottom: 16px; }
  .header-row { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
  .nav { margin-bottom: 12px; font-size: 13px; }
  .nav a { color: var(--accent); text-decoration: none; } .nav a:hover { text-decoration: underline; }
  .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 12px; }
  .card { background: var(--card); border: 1px solid var(--border); border-radius: 8px; overflow: hidden; cursor: pointer; transition: border-color 0.2s; }
  .card:hover { border-color: var(--accent); }
  .card.selected { border-color: var(--green); border-width: 2px; }
  .card-thumb { width: 100%; aspect-ratio: 16/9; object-fit: cover; object-position: bottom left; display: block; }
  .card-info { padding: 8px 12px; font-size: 12px; }
  .card-info .ts { color: var(--accent); font-weight: 600; }
  .card-info .mode { color: var(--dim); }
  .card-info .match-name { color: var(--text); font-weight: 600; font-size: 13px; }
  .detail { background: var(--card); border: 1px solid var(--border); border-radius: 8px; padding: 16px; margin-bottom: 16px; display: none; }
  .detail.active { display: block; }
  .detail-header { margin-bottom: 12px; }
  .detail-header h2 { color: var(--text); font-size: 16px; margin-bottom: 2px; }
  .detail-header .replay-path { font-family: 'Consolas', monospace; font-size: 11px; color: var(--dim); cursor: pointer; }
  .detail-header .replay-path:hover { color: var(--accent); }
  .detail-body { display: flex; gap: 16px; }
  .detail-img { flex: 0 0 auto; max-width: 55%; }
  .detail-img img { width: 100%; border-radius: 4px; border: 1px solid var(--border); }
  .detail-state { flex: 1; font-size: 13px; overflow-y: auto; max-height: 600px; }
  .detail-state h3 { color: var(--accent); margin: 8px 0 4px; font-size: 14px; }
  .kv { display: flex; gap: 8px; padding: 2px 0; border-bottom: 1px solid var(--border); }
  .kv .k { color: var(--dim); min-width: 120px; }
  .kv .v { color: var(--text); }
  .badge { display: inline-block; padding: 1px 6px; border-radius: 3px; font-size: 11px; font-weight: 700; }
  .badge-bb { background: rgba(86,39,173,0.2); color: #a855f7; }
  .badge-fight { background: rgba(248,81,73,0.2); color: var(--red); }
  .empty { color: var(--dim); text-align: center; padding: 60px; }
  .clear-btn { background: var(--card); border: 1px solid var(--border); color: var(--red); padding: 6px 14px; border-radius: 6px; cursor: pointer; font-size: 13px; }
  .clear-btn:hover { border-color: var(--red); }
</style>
</head>
<body>
<div class="nav"><a href="/">Tournaments</a> | <a href="/tests">Tests</a> | <strong>Screenshots</strong></div>
<div class="header-row">
  <div><h1>Screenshots</h1><p class="subtitle">Press F12 in replay viewer to capture screenshots with game state</p></div>
  <button class="clear-btn" onclick="clearAll()">Clear All Screenshots</button>
</div>

<div id="detail" class="detail"></div>
<div id="grid" class="grid"></div>

<script>
let screenshots = [];
let selectedIdx = -1;

async function load() {
  try {
    const r = await fetch('/api/screenshots');
    screenshots = await r.json();
    renderGrid();
  } catch(e) {
    document.getElementById('grid').innerHTML = `<div class="empty">No screenshots yet. Open a replay and press F12.</div>`;
  }
}

async function clearAll() {
  if (!confirm('Delete all screenshots?')) return;
  await fetch('/api/screenshots/clear', { method: 'POST' });
  screenshots = [];
  selectedIdx = -1;
  document.getElementById('detail').classList.remove('active');
  document.getElementById('detail').innerHTML = '';
  renderGrid();
}

function renderGrid() {
  const el = document.getElementById('grid');
  if (screenshots.length === 0) {
    el.innerHTML = '<div class="empty">No screenshots yet. Open a replay and press F12.</div>';
    return;
  }
  el.innerHTML = screenshots.map((s, i) => {
    const st = s.state;
    const mode = st.is_beacon_brawl ? 'BB' : 'Fight';
    const modeBadge = st.is_beacon_brawl ? 'badge-bb' : 'badge-fight';
    const tick = `${st.tick}/${st.total_ticks}`;
    const mapVal = st.run_config?.map || 'flat';
    const isFlat = mapVal.includes('flat') || mapVal.includes('no modifier');
    const matchName = st.match_name || '';
    return `
    <div class="card" id="card-${i}" onclick="selectScreenshot(${i})">
      ${s.has_image ? `<img class="card-thumb" src="/api/screenshots/img/${s.name}.png" loading="lazy">` : '<div style="height:180px;background:#0a0e14;display:flex;align-items:center;justify-content:center;color:var(--dim)">No image</div>'}
      <div class="card-info">
        ${matchName ? `<div class="match-name">${matchName}</div>` : ''}
        <span class="badge ${modeBadge}">${mode}</span>
        <span class="mode">Tick ${tick} (${st.time_seconds?.toFixed(1) || '?'}s)</span>
        <br><span style="color:${isFlat ? 'var(--dim)' : '#3fb950'};font-size:11px">${mapVal}</span>
      </div>
    </div>`;
  }).join('');
}

function selectScreenshot(idx) {
  if (selectedIdx >= 0) document.getElementById('card-' + selectedIdx)?.classList.remove('selected');
  selectedIdx = idx;
  document.getElementById('card-' + idx)?.classList.add('selected');

  const s = screenshots[idx];
  const st = s.state;
  const detail = document.getElementById('detail');
  detail.classList.add('active');

  // Header: match name + replay path
  const matchName = st.match_name || s.name;
  const replayFile = st.replay_file || '';
  const shortPath = replayFile.replace(/\\/g, '/').split('/').slice(-4).join('/');

  let stateHtml = '';

  // Run config
  if (st.run_config) {
    stateHtml += '<h3>Run Config</h3>';
    const rc = st.run_config;
    const mapVal = rc.map || 'flat (no modifiers)';
    const isFlat = mapVal.includes('flat') || mapVal.includes('no modifier');
    stateHtml += `<div class="kv"><span class="k" style="font-weight:700">Map</span><span class="v" style="color:${isFlat ? 'var(--dim)' : '#3fb950'};font-weight:600">${mapVal}</span></div>`;
    for (const [k, v] of Object.entries(rc)) {
      if (k === 'map') continue;
      stateHtml += `<div class="kv"><span class="k">${k}</span><span class="v">${typeof v === 'object' ? JSON.stringify(v) : String(v)}</span></div>`;
    }
  }

  // Positions
  if (st.positions) {
    stateHtml += '<h3>Positions</h3>';
    if (st.positions.pawns) {
      stateHtml += st.positions.pawns.map(p =>
        `<div class="kv"><span class="k">Pawn ${p.index} (T${p.team})</span><span class="v">x:${p.x?.toFixed(0)} y:${p.y?.toFixed(0)} hp:${p.hp?.toFixed(0)} ${p.dead?'DEAD ':''}${p.stunned?'STUN ':''}${p.vulnerable?'VULN ':''}</span></div>`
      ).join('');
      if (st.positions.scores) stateHtml += `<div class="kv"><span class="k">Scores</span><span class="v">${st.positions.scores.join(' - ')}</span></div>`;
      if (st.positions.kills) stateHtml += `<div class="kv"><span class="k">Kills</span><span class="v">${st.positions.kills.join(' - ')}</span></div>`;
    } else {
      stateHtml += renderKV(st.positions);
    }
  }

  // Feature state
  if (st.feature_state) {
    stateHtml += '<h3>Feature State</h3>';
    if (st.feature_state.beacons) {
      const bNames = ['Left', 'Center', 'Right'];
      stateHtml += st.feature_state.beacons.map((b, i) =>
        `<div class="kv"><span class="k">Beacon ${bNames[i] || i}</span><span class="v">Owner:${b.owner===1?'A':b.owner===2?'B':'-'} Cap:${b.capture_progress} ${b.contested?'CONTESTED':''}</span></div>`
      ).join('');
    } else {
      stateHtml += renderKV(st.feature_state);
    }
  }

  // Meta
  stateHtml += '<h3>Meta</h3>';
  stateHtml += `<div class="kv"><span class="k">Tick</span><span class="v">${st.tick} / ${st.total_ticks} (${st.time_seconds?.toFixed(1)}s)</span></div>`;
  stateHtml += `<div class="kv"><span class="k">Over</span><span class="v">${st.is_over ? 'Yes — Winner: ' + st.winner : 'No'}</span></div>`;
  if (st.build) stateHtml += `<div class="kv"><span class="k">Build</span><span class="v" style="color:var(--dim)">${st.build}</span></div>`;

  detail.innerHTML = `
    <div class="detail-header">
      <h2>${matchName}</h2>
      ${replayFile ? `<span class="replay-path" title="${replayFile}" onclick="navigator.clipboard.writeText('${replayFile.replace(/\\/g, '\\\\\\\\')}'); this.textContent='copied!'; setTimeout(() => this.textContent='${shortPath}', 1200)">${shortPath}</span>` : ''}
    </div>
    <div class="detail-body">
      ${s.has_image ? `<div class="detail-img"><img src="/api/screenshots/img/${s.name}.png"></div>` : ''}
      <div class="detail-state">${stateHtml}</div>
    </div>
  `;
}

function renderKV(obj) {
  if (!obj || typeof obj !== 'object') return '';
  return Object.entries(obj).map(([k, v]) => {
    const val = typeof v === 'object' ? JSON.stringify(v) : String(v);
    return `<div class="kv"><span class="k">${k}</span><span class="v">${val}</span></div>`;
  }).join('');
}

load();
</script>
</body>
</html>
""";
}
