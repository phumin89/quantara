import assets from 'virtual:quantara-office-assets';

type Agent = { id: number; label: string; status: string };
const statuses = new Set([
  'idle',
  'queued',
  'active',
  'blocked',
  'failed',
  'cancelled',
  'completed',
  'unknown',
  'stale',
]);
const roles = [
  'CEO',
  'Analyst',
  'Reviewer',
  'Intraday',
  'Swing',
  'News',
  'Positions',
  'Quant',
];
let agents: Agent[] = roles.map((label, i) => ({
  id: i + 1,
  label,
  status: 'unknown',
}));
let selectedId: number | null = null;
let paused = true;
const handlers = new Set<(message: unknown) => void>();
const motionHandlers = new Set<() => void>();
const media = matchMedia('(prefers-reduced-motion: reduce)');
const deliver = (message: unknown) =>
  handlers.forEach((handler) => handler(message));
const notifyMotion = () => motionHandlers.forEach((handler) => handler());
export const requestOfficeRender = notifyMotion;
const tellParent = (type: string, extra = {}) =>
  parent.postMessage(
    { source: 'pixel-agents-office', type, ...extra },
    location.origin,
  );

export const officeRoleLabel = (id: number) => {
  const agent = agents.find((item) => item.id === id);
  return agent ? `${agent.label}: ${agent.status}` : 'Research role';
};
export const officeMotionEnabled = () =>
  !paused && !document.hidden && !media.matches;
export const subscribeOfficeMotion = (handler: () => void) => {
  motionHandlers.add(handler);
  return () => motionHandlers.delete(handler);
};
media.addEventListener('change', notifyMotion);
document.addEventListener('visibilitychange', notifyMotion);
// Native canvas resize/zoom/pan must repaint even while character motion is paused.
new ResizeObserver(notifyMotion).observe(document.documentElement);
for (const type of [
  'pointermove',
  'pointerdown',
  'pointerup',
  'wheel',
  'keydown',
  'click',
])
  document.addEventListener(type, notifyMotion, {
    capture: true,
    passive: true,
  });

function project(emitSelection = true) {
  agents.forEach(({ id, status }) =>
    deliver({ type: 'agentStatus', id, status }),
  );
  if (emitSelection && selectedId !== null) {
    deliver({ type: 'agentSelected', id: selectedId });
  }
  notifyMotion();
}

function bootstrap() {
  deliver({ type: 'officeAccess', readOnly: true });
  deliver({
    type: 'terminalAvailability',
    available: false,
    reason: 'Research office has no terminal access.',
  });
  deliver({
    type: 'providerCapabilities',
    readingTools: [],
    subagentToolNames: [],
  });
  deliver({
    type: 'settingsLoaded',
    soundEnabled: false,
    hooksEnabled: false,
    hooksInfoShown: true,
    watchAllSessions: false,
    alwaysShowLabels: false,
    bypassPermissions: false,
    showHostDirectories: false,
    externalAssetDirectories: [],
    extensionVersion: '1.5.0',
    lastSeenVersion: '1.5',
  });
  deliver({ type: 'characterSpritesLoaded', characters: assets.characters });
  deliver({ type: 'floorTilesLoaded', sprites: assets.floors });
  deliver({ type: 'wallTilesLoaded', sets: assets.walls });
  deliver({ type: 'carpetTilesLoaded', sets: assets.carpets });
  deliver({
    type: 'furnitureAssetsLoaded',
    catalog: assets.catalog,
    sprites: assets.furniture,
  });
  deliver({ type: 'layoutLoaded', layout: assets.layout });
  deliver({
    type: 'existingAgents',
    agents: agents.map(({ id }) => id),
    agentMeta: Object.fromEntries(
      agents.map(({ id }) => [
        id,
        { palette: (id - 1) % 6, hueShift: id > 6 ? 40 : 0 },
      ]),
    ),
  });
  project();
  tellParent('ready');
}

window.addEventListener('message', (event: MessageEvent) => {
  if (event.source !== parent || event.origin !== location.origin) return;
  const data = event.data;
  if (data?.source === 'quantara-home' && data.type === 'request-ready') {
    if (handlers.size > 0) tellParent('ready');
    return;
  }
  if (
    data?.source === 'quantara-home' &&
    data.type === 'select' &&
    Number.isInteger(data.id) &&
    data.id >= 1 &&
    data.id <= 8
  ) {
    selectedId = data.id;
    deliver({ type: 'agentSelected', id: selectedId });
    notifyMotion();
    return;
  }
  if (
    !data ||
    data.source !== 'quantara-home' ||
    data.type !== 'snapshot' ||
    !Array.isArray(data.agents) ||
    data.agents.length !== 8 ||
    !data.agents.every(
      (agent: unknown) =>
        agent &&
        typeof agent === 'object' &&
        Number.isInteger((agent as Agent).id) &&
        (agent as Agent).id >= 1 &&
        (agent as Agent).id <= 8 &&
        typeof (agent as Agent).label === 'string' &&
        (agent as Agent).label.length <= 32 &&
        statuses.has((agent as Agent).status),
    ) ||
    new Set(data.agents.map((agent: Agent) => agent.id)).size !== 8 ||
    typeof data.paused !== 'boolean' ||
    !(
      data.selectedId === null ||
      (Number.isInteger(data.selectedId) &&
        data.selectedId >= 1 &&
        data.selectedId <= 8)
    )
  )
    return;
  agents = data.agents.map(({ id, label, status }: Agent) => ({
    id,
    label,
    status,
  }));
  const selectionChanged = selectedId !== data.selectedId;
  selectedId = data.selectedId;
  paused = data.paused;
  project(selectionChanged);
});

export const transport = {
  state: 'connected' as const,
  readyPromise: Promise.resolve(),
  send(message: { type: string; id?: number }) {
    if (message.type === 'webviewReady') bootstrap();
    if (
      message.type === 'focusAgent' &&
      agents.some((agent) => agent.id === message.id)
    )
      tellParent('select', { id: message.id });
    // Every host, filesystem, hook, launch and terminal command is discarded.
  },
  onMessage(handler: (message: unknown) => void) {
    handlers.add(handler);
    return () => handlers.delete(handler);
  },
  onStateChange() {
    return () => {};
  },
  dispose() {
    handlers.clear();
  },
};
