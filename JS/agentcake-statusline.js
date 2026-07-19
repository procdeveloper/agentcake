// AgentCake statusLine hook.
// Claude Code pipes its status JSON to this script's stdin on every refresh.
// We persist the raw payload where the AgentCake tray app reads it, and echo a
// compact line back so the Claude Code status bar still shows something useful.

const fs = require('fs');
const os = require('os');
const path = require('path');

let data = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', c => (data += c));
process.stdin.on('end', () => {
  // 1) Persist the raw payload for the tray app.
  try {
    const roaming =
      process.env.APPDATA || path.join(os.homedir(), 'AppData', 'Roaming');
    const dir = path.join(roaming, 'AgentCake');
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(path.join(dir, 'status.json'), data);
  } catch (e) {
    /* never let a write error break the status bar */
  }

  // 2) Echo a compact status line (model + 5h/weekly % when present).
  let line = 'AgentCake';
  try {
    const j = JSON.parse(data);
    const model = j.model && j.model.display_name ? j.model.display_name : '';
    const rl = j.rate_limits || j.rate_limit || null;
    const fh = rl && (rl.five_hour || rl.session) ;
    const sd = rl && (rl.seven_day || rl.weekly);
    const pct = o =>
      o == null ? null : o.used_percentage != null ? o.used_percentage : o.utilization;
    const f = pct(fh);
    const w = pct(sd);
    const parts = [];
    if (model) parts.push(model);
    if (f != null) parts.push('5h ' + Math.round(f) + '%');
    if (w != null) parts.push('wk ' + Math.round(w) + '%');
    if (parts.length) line = parts.join('  \u00b7  ');
  } catch (e) {
    /* malformed payload -> default label */
  }
  process.stdout.write(line);
});
