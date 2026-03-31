namespace AiBtGym.Godot;

internal static class BtCompareHtml
{
    internal static string Build() => """
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
}
