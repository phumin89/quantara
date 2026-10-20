import { execFileSync } from 'node:child_process';
import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';

const pin = 'b1bafd02896d90d83457266575a1e8e5011ce997';
const source = path.resolve(process.argv[2] ?? '');
const git = (...args) =>
  execFileSync('git', ['-C', source, ...args], { encoding: 'utf8' });
if (!process.argv[2] || git('rev-parse', 'HEAD').trim() !== pin)
  throw new Error(
    'Supply the reviewed upstream checkout at the pinned commit.',
  );
if (git('status', '--porcelain', '--untracked-files=all').trim())
  throw new Error(
    'The pinned upstream checkout must be clean before vendoring.',
  );
const files = git('ls-files', '-z')
  .split('\0')
  .filter(
    (file) =>
      /^(core\/src\/|webview-ui\/src\/|webview-ui\/public\/(assets|fonts)\/)/.test(
        file,
      ) ||
      ['webview-ui/index.html', 'webview-ui/package.json', 'LICENSE'].includes(
        file,
      ),
  );
const manifest = {
  repository: 'https://github.com/pixel-agents-hq/pixel-agents',
  commit: pin,
  files: [],
};
for (const file of files) {
  const destination = `vendor/pixel-agents/upstream/${file}`;
  await mkdir(path.dirname(destination), { recursive: true });
  await copyFile(path.join(source, file), destination);
  manifest.files.push({
    path: destination,
    upstreamPath: file,
    sha256: createHash('sha256')
      .update(await readFile(destination))
      .digest('hex'),
    license: file.includes('/characters/') ? 'CC0-1.0 + MIT adaptation' : 'MIT',
  });
}
await copyFile(path.join(source, 'LICENSE'), 'vendor/pixel-agents/LICENSE');
await writeFile(
  'vendor/pixel-agents/manifest.json',
  JSON.stringify(manifest, null, 2) + '\n',
);
console.log(
  `Vendored ${files.length} byte-identical upstream UI/core/assets files at ${pin}.`,
);
