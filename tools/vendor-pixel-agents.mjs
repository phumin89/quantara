import { build } from 'esbuild';
import { execFileSync } from 'node:child_process';
import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import path from 'node:path';

const pin = 'b1bafd02896d90d83457266575a1e8e5011ce997';
const source = path.resolve(process.argv[2] ?? '');
if (
  !process.argv[2] ||
  execFileSync('git', ['-C', source, 'rev-parse', 'HEAD'], {
    encoding: 'utf8',
  }).trim() !== pin
)
  throw new Error(
    'Supply the reviewed upstream checkout at the pinned commit.',
  );
if (
  execFileSync(
    'git',
    ['-C', source, 'status', '--porcelain', '--untracked-files=all'],
    { encoding: 'utf8' },
  ).trim()
)
  throw new Error(
    'The pinned upstream checkout must be clean before vendoring.',
  );
const result = await build({
  entryPoints: ['vendor/pixel-agents/PixelAgentsOffice.ts'],
  bundle: true,
  platform: 'browser',
  format: 'esm',
  write: false,
  metafile: true,
  plugins: [
    {
      name: 'reviewed-upstream',
      setup(builder) {
        builder.onResolve({ filter: /^\.\/upstream\// }, (args) => ({
          path: path.join(
            source,
            args.path.slice('./upstream/'.length).replace(/\.js$/, '.ts'),
          ),
        }));
      },
    },
  ],
});
const manifest = {
  repository: 'https://github.com/pixel-agents-hq/pixel-agents',
  commit: pin,
  files: [],
};
const copy = async (relative, destination, license) => {
  const input = path.join(source, relative);
  await mkdir(path.dirname(destination), { recursive: true });
  await copyFile(input, destination);
  manifest.files.push({
    path: destination.replaceAll('\\', '/'),
    upstreamPath: relative.replaceAll('\\', '/'),
    sha256: createHash('sha256')
      .update(await readFile(input))
      .digest('hex'),
    license,
  });
};
for (const input of Object.keys(result.metafile.inputs)) {
  const absolute = path.resolve(input);
  if (!absolute.startsWith(source + path.sep)) continue;
  const relative = path.relative(source, absolute);
  if (
    /server[\\/]|transport[\\/]|browserMock|App\.tsx|hooks[\\/]/i.test(relative)
  )
    throw new Error('Privileged upstream dependency refused: ' + relative);
  await copy(
    relative,
    path.join('vendor/pixel-agents/upstream', relative),
    'MIT',
  );
}
await copy('LICENSE', 'vendor/pixel-agents/LICENSE', 'MIT');
await copy('LICENSE', 'apps/web/public/vendor/pixel-agents/LICENSE', 'MIT');
for (let index = 0; index < 6; index++)
  await copy(
    `webview-ui/public/assets/characters/char_${index}.png`,
    `apps/web/public/vendor/pixel-agents/char_${index}.png`,
    'CC0-1.0 + MIT adaptation',
  );
await writeFile(
  'vendor/pixel-agents/manifest.json',
  JSON.stringify(manifest, null, 2) + '\n',
);
console.log(`Vendored ${manifest.files.length} reviewed files at ${pin}.`);
