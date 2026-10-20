import { build } from 'esbuild';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';

const manifest = JSON.parse(
  await readFile('vendor/pixel-agents/manifest.json', 'utf8'),
);
for (const file of manifest.files) {
  const hash = createHash('sha256')
    .update(await readFile(file.path))
    .digest('hex');
  if (hash !== file.sha256)
    throw new Error('Vendored pixel-agents integrity mismatch: ' + file.path);
}
const result = await build({
  entryPoints: ['vendor/pixel-agents/PixelAgentsOffice.ts'],
  bundle: true,
  platform: 'browser',
  format: 'esm',
  target: 'es2020',
  minify: true,
  legalComments: 'inline',
  outfile: 'apps/web/public/vendor/pixel-agents/renderer.js',
  metafile: true,
  banner: {
    js: '/*! Pixel Agents renderer: MIT (c) 2026 Pablo De Lucca. See ./LICENSE and ./NOTICE.txt. */',
  },
});
for (const input of Object.keys(result.metafile.inputs))
  if (/server[\\/]|transport[\\/]|browserMock|App\.tsx|hooks[\\/]/i.test(input))
    throw new Error('Privileged renderer dependency refused: ' + input);
console.log(
  `Pixel Agents renderer built from ${Object.keys(result.metafile.inputs).length} local inputs; no transports or server modules.`,
);
