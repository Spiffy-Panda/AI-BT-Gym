namespace AiBtGym.Godot;

internal static class ReplayViewerHtml
{
    internal static string Build() => """
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
}
