import { build as bundle } from 'esbuild';
import { build } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import { copyFile, mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

const root = path.resolve('vendor/pixel-agents');
const upstream = path.join(root, 'upstream/webview-ui');
const bridge = path.join(root, 'integration/transport.ts');
const disabled = path.join(root, 'integration/disabled.tsx');
const out = path.resolve('apps/web/public/vendor/pixel-agents/office');
const manifest = JSON.parse(
  await readFile(path.join(root, 'manifest.json'), 'utf8'),
);
for (const file of manifest.files) {
  const hash = createHash('sha256')
    .update(await readFile(file.path))
    .digest('hex');
  if (hash !== file.sha256)
    throw new Error('Upstream integrity mismatch: ' + file.path);
}
await mkdir('work/pixel-agents', { recursive: true });
const decoder = path.resolve('work/pixel-agents/assets.cjs');
await bundle({
  entryPoints: [path.join(root, 'integration/assets.ts')],
  bundle: true,
  platform: 'node',
  format: 'cjs',
  outfile: decoder,
  logLevel: 'warning',
});
const assets = createRequire(import.meta.url)(decoder).loadOfficeAssets(
  path.join(upstream, 'public/assets'),
);
if (
  assets.characters.length !== 6 ||
  !assets.catalog.length ||
  !Object.keys(assets.furniture).length
)
  throw new Error('Original office assets are incomplete.');
const adapted = [];
const replace = (code, before, after, id) => {
  if (!code.includes(before))
    throw new Error('Pinned adaptation no longer matches: ' + id);
  return code.replace(before, after);
};
const csp =
  "default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'";
await build({
  configFile: false,
  root: upstream,
  base: '/vendor/pixel-agents/office/',
  publicDir: false,
  plugins: [
    {
      name: 'quantara-read-only-office',
      enforce: 'pre',
      resolveId(id) {
        if (id === 'virtual:quantara-office-assets')
          return '\0quantara-office-assets';
        if (/\/transport\/index(?:\.js)?$/.test(id)) return bridge;
        if (
          /\/(?:TerminalPane|MobileTerminalPage)(?:\.js)?$/.test(id) ||
          /\/terminal\//.test(id)
        )
          return disabled;
        if (id === '@xterm/xterm/css/xterm.css')
          return path.join(root, 'integration/empty.css');
      },
      load(id) {
        if (id === '\0quantara-office-assets')
          return `export default ${JSON.stringify(assets)};`;
      },
      transform(code, id) {
        const file = id.replaceAll('\\', '/');
        if (file.endsWith('/office/engine/gameLoop.ts')) {
          adapted.push('gameLoop: pause and reduced motion');
          code =
            `import { officeMotionEnabled, subscribeOfficeMotion } from ${JSON.stringify(bridge)};\n` +
            code;
          code = replace(
            code,
            'callbacks.update(dt);',
            'if (officeMotionEnabled()) callbacks.update(dt);',
            id,
          );
          code = replace(
            code,
            'rafId = requestAnimationFrame(frame);\n  };',
            'if (officeMotionEnabled()) rafId = requestAnimationFrame(frame);\n  };',
            id,
          );
          code = replace(
            code,
            '  rafId = requestAnimationFrame(frame);\n\n  return',
            '  const unsubscribe = subscribeOfficeMotion(() => { cancelAnimationFrame(rafId); lastTime = 0; rafId = requestAnimationFrame(frame); });\n  rafId = requestAnimationFrame(frame);\n\n  return',
            id,
          );
          code = replace(
            code,
            'stopped = true;',
            'stopped = true; unsubscribe();',
            id,
          );
        }
        if (file.endsWith('/hooks/useTerminalDrawer.ts')) {
          adapted.push('native card status: recorded research semantics');
          code = replace(
            code,
            "if (agentStatuses[id] === 'waiting') return 'idle';",
            "if (agentStatuses[id] === 'completed') return 'idle';\n      if (['failed', 'blocked'].includes(agentStatuses[id])) return 'attention';\n      if (agentStatuses[id]) return null;",
            id,
          );
        }
        if (file.endsWith('/hooks/useExtensionMessages.ts')) {
          adapted.push('parent selection: native camera');
          code = replace(
            code,
            "      } else if (msg.type === 'agentSelected') {\n        const id = msg.id as number;\n        setSelectedAgent(id);\n      } else if (msg.type === 'agentStatus') {",
            "      } else if (msg.type === 'agentSelected') {\n        const id = msg.id as number;\n        setSelectedAgent(id);\n        os.selectAndFollow(id);\n      } else if (msg.type === 'agentStatus') {",
            id,
          );
        }
        if (file.endsWith('/mobile/useMobileShell.ts')) {
          adapted.push('mobile card selection: parent role synchronization');
          code =
            `import { transport } from ${JSON.stringify(bridge)};\n` + code;
          code = replace(
            code,
            '(agentId: number) => {\n      const os = getOfficeState();',
            "(agentId: number) => {\n      transport.send({ type: 'focusAgent', id: agentId });\n      const os = getOfficeState();",
            id,
          );
        }
        if (file.endsWith('/components/AgentCard.tsx')) {
          adapted.push(
            'native cards: role labels and keyboard access, no close authority',
          );
          code =
            `import { officeRoleLabel } from ${JSON.stringify(bridge)};\n` +
            code;
          code = replace(code, 'showClose = true', 'showClose = false', id);
          code = replace(
            code,
            'title={`Agent ${agentId}`}',
            'title={officeRoleLabel(agentId)} role="button" tabIndex={0} aria-label={officeRoleLabel(agentId)} onKeyDown={(event) => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); onSelect(agentId); } }}',
            id,
          );
        }
        if (file.endsWith('/office/components/OfficeCanvas.tsx')) {
          adapted.push('paused camera: immediate focus and resize repaint');
          code =
            `import { officeMotionEnabled, requestOfficeRender } from ${JSON.stringify(bridge)};\n` +
            code;
          code = replace(
            code,
            'Math.abs(dx) < CAMERA_FOLLOW_SNAP_THRESHOLD &&\n            Math.abs(dy) < CAMERA_FOLLOW_SNAP_THRESHOLD',
            '!officeMotionEnabled() || (Math.abs(dx) < CAMERA_FOLLOW_SNAP_THRESHOLD &&\n            Math.abs(dy) < CAMERA_FOLLOW_SNAP_THRESHOLD)',
            id,
          );
          code = replace(
            code,
            'canvas.style.height = `${rect.height}px`;\n    // No ctx.scale(dpr) — we render directly in device pixels',
            'canvas.style.height = `${rect.height}px`;\n    requestOfficeRender();\n    // No ctx.scale(dpr) — we render directly in device pixels',
            id,
          );
          adapted.push('mobile touch selection: parent role synchronization');
          code = replace(
            code,
            '          } else {\n            officeState.selectedAgentId = hitId;\n            officeState.cameraFollowId = hitId;\n          }\n          return;',
            "          } else {\n            officeState.selectedAgentId = hitId;\n            officeState.cameraFollowId = hitId;\n            transport.send({ type: 'focusAgent', id: hitId });\n          }\n          return;",
            id,
          );
        }
        if (file.endsWith('/index.css'))
          code = code.replace('/fonts/', '/vendor/pixel-agents/office/fonts/');
        return code;
      },
      transformIndexHtml: {
        order: 'pre',
        handler(html) {
          return html
            .replace(/<link[^>]+>/g, '')
            .replace('user-scalable=no, ', '')
            .replace(
              '<head>',
              `<head><meta http-equiv="Content-Security-Policy" content="${csp}">`,
            );
        },
      },
      generateBundle(_options, output) {
        for (const chunk of Object.values(output))
          if (chunk.type === 'chunk') {
            for (const id of Object.keys(chunk.modules))
              if (
                /\/transport\/(?:webSocket|postMessage)|browserMock|\/terminal\/|@xterm|\/server\//i.test(
                  id,
                )
              )
                throw new Error('Privileged office dependency refused: ' + id);
          }
      },
    },
    tailwindcss(),
    react(),
  ],
  build: { outDir: out, emptyOutDir: true, sourcemap: false, target: 'es2020' },
});
await mkdir(path.join(out, 'fonts'), { recursive: true });
await copyFile(
  path.join(upstream, 'public/fonts/FSPixelSansUnicode-Regular.ttf'),
  path.join(out, 'fonts/FSPixelSansUnicode-Regular.ttf'),
);
await copyFile(path.join(root, 'LICENSE'), path.join(out, 'LICENSE'));
await writeFile(
  path.join(out, 'provenance.json'),
  JSON.stringify(
    {
      repository: manifest.repository,
      commit: manifest.commit,
      originalApp: 'webview-ui/src/App.tsx',
      originalLayout: 'webview-ui/public/assets/default-layout-1.json',
      adaptations: adapted,
      readOnly: true,
      network: 'connect-src none',
    },
    null,
    2,
  ) + '\n',
);
console.log(
  `Original Pixel Agents App, office layout and ${assets.catalog.length} furniture entries built read-only.`,
);
