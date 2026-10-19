const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');

const root = path.resolve(__dirname, '..');
const ignored = new Set([
  '.git',
  '.next',
  'node_modules',
  'work',
  'bin',
  'obj',
  '.runtime',
]);
const gitFiles = (args) =>
  execFileSync('git', args, { cwd: root, encoding: 'utf8' })
    .split(/\r?\n/)
    .filter(Boolean);
const files = [
  ...new Set([
    ...gitFiles(['ls-files']),
    ...gitFiles(['ls-files', '--others', '--exclude-standard']),
  ]),
].filter(
  (file) =>
    file.toLowerCase().endsWith('.md') &&
    fs.existsSync(path.join(root, file)) &&
    !file.split(/[\\/]/).some((part) => ignored.has(part)),
);
const failures = [];
const linkPattern = /!?\[[^\]]*\]\(([^)]+)\)/g;

for (const relative of files) {
  const full = path.join(root, relative);
  const text = fs.readFileSync(full, 'utf8');
  if (!text.trim()) failures.push(`${relative}: whitespace-only file`);
  for (const match of text.matchAll(linkPattern)) {
    let target = match[1].trim().replace(/^<|>$/g, '');
    if (!target || /^(?:[a-z][a-z\d+.-]*:|\/|\\\\|[a-z]:[\\/])/i.test(target))
      continue;
    target = target.split('#', 1)[0];
    if (!target) continue;
    try {
      target = decodeURIComponent(target);
    } catch {
      failures.push(`${relative}: invalid link encoding: ${match[1]}`);
      continue;
    }
    const resolved = path.resolve(path.dirname(full), target);
    if (!resolved.startsWith(`${root}${path.sep}`) && resolved !== root) {
      failures.push(`${relative}: link escapes repository: ${match[1]}`);
    } else if (!fs.existsSync(resolved)) {
      failures.push(`${relative}: missing link target: ${match[1]}`);
    }
  }
}

if (failures.length) {
  console.error(failures.join('\n'));
  process.exitCode = 1;
} else {
  console.log(`Docs: ${files.length} Markdown files checked.`);
}
