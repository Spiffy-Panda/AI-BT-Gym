namespace AiBtGym.Godot;

internal static class TestPageHtml
{
    internal static string Build() => """
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
<div class="nav"><a href="/">Tournaments</a> | <strong>Tests</strong> | <a href="/screenshots">Screenshots</a></div>
<div class="header">
  <div><h1>Tests</h1><p class="subtitle">Movement tests with visual replay + Map self-play tests with Godot replay</p></div>
  <div style="display:flex;align-items:center"><button id="runBtn" onclick="runTests()">Run All Tests</button><span id="statusText" class="status"></span></div>
</div>
<h2 style="color:var(--accent);margin:16px 0 8px">Movement Tests</h2>
<div id="results"></div>
<h2 style="color:var(--accent);margin:16px 0 8px">Map Self-Play Tests</h2>
<div id="mapResults"></div>
<h2 style="color:var(--accent);margin:16px 0 8px">Beacon Brawl Map Tests (TestTeam Self-Play)</h2>
<div id="bbResults"></div>

<script>
let testData = null;
let mapTestData = null;
let bbTestData = null;
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
    bbTestData = data.bb_results || [];
    const totalP = data.passed + (data.map_passed || 0) + (data.bb_passed || 0);
    const totalF = data.failed + (data.map_failed || 0) + (data.bb_failed || 0);
    status.textContent = `${totalP} passed, ${totalF} failed in ${data.elapsed_ms}ms`;
    renderResults();
    renderMapResults();
    renderBbResults();
  } catch(e) { status.textContent = 'Error: ' + e.message; }
  btn.disabled = false;
}

async function loadCached() {
  try {
    const [movR, mapR, bbR] = await Promise.all([
      fetch('/api/tests/results'),
      fetch('/api/tests/map-results'),
      fetch('/api/tests/bb-map-results')
    ]);
    const results = await movR.json();
    if (results.length > 0) {
      const passed = results.filter(r => r.passed).length;
      const failed = results.filter(r => !r.passed).length;
      testData = { passed, failed, results };
      renderResults();
    }
    const mapResults = await mapR.json();
    if (mapResults.length > 0) { mapTestData = mapResults; renderMapResults(); }
    const bbResults = await bbR.json();
    if (bbResults.length > 0) { bbTestData = bbResults; renderBbResults(); }
    // Update combined status
    const tp = (testData?.passed || 0) + (mapTestData ? mapTestData.filter(r => r.passed).length : 0) + (bbTestData ? bbTestData.filter(r => r.passed).length : 0);
    const tf = (testData?.failed || 0) + (mapTestData ? mapTestData.filter(r => !r.passed).length : 0) + (bbTestData ? bbTestData.filter(r => !r.passed).length : 0);
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

function renderBbResults() {
  if (!bbTestData) return;
  const el = document.getElementById('bbResults');
  el.innerHTML = bbTestData.map(t => {
    const outcome = t.feature_notes?.outcome || '';
    const duration = t.feature_notes?.duration || `${t.duration_ticks} ticks`;
    const scores = t.final_scores ? `${t.final_scores[0]}-${t.final_scores[1]}` : '';
    const kills = t.kills ? `kills: ${t.kills[0]}-${t.kills[1]}` : '';
    const notes = Object.entries(t.feature_notes || {})
      .filter(([k]) => k !== 'outcome' && k !== 'duration' && k !== 'scores' && k !== 'kills')
      .map(([k, v]) => `<span class="metric"><span>${k}:</span> ${v}</span>`)
      .join(' ');
    return `
    <div class="card" style="cursor:default">
      <div style="display:flex;justify-content:space-between;align-items:center">
        <h2>${t.map_name}</h2>
        <div style="display:flex;gap:8px;align-items:center">
          ${t.has_replay ? `<button onclick="event.stopPropagation(); launchBbReplay('${t.map_name}')" style="background:#3fb950;padding:6px 14px;font-size:12px">Watch in Godot</button>` : ''}
          <span class="badge ${t.passed ? 'badge-pass' : 'badge-fail'}">${t.passed ? 'PASS' : 'FAIL'}</span>
        </div>
      </div>
      ${t.error ? `<div class="error-msg">${t.error}</div>` : ''}
      <div style="color:var(--dim);font-size:13px;margin-top:4px">${outcome} &mdash; ${duration} &mdash; ${scores} ${kills}</div>
      <div style="margin-top:6px;line-height:1.8">${notes}</div>
    </div>`;
  }).join('');
}

async function launchBbReplay(mapName) {
  try {
    const r = await fetch('/api/tests/bb-map-launch', {
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
}
