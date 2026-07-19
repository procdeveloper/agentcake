// Captures the live Claude Code status payload for AgentCake.
const fs = require('fs');
const os = require('os');
const path = require('path');

let data = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', chunk => (data += chunk));
process.stdin.on('end', () => {
  try {
    const roaming = process.env.APPDATA || path.join(os.homedir(), 'AppData', 'Roaming');
    const dir = path.join(roaming, 'AgentCake');
    const target = path.join(dir, 'claude-status.json');
    fs.mkdirSync(dir, { recursive: true });
    fs.writeFileSync(`${target}.tmp`, data, 'utf8');
    fs.renameSync(`${target}.tmp`, target);
  } catch (_) { }
  process.stdout.write('');
});