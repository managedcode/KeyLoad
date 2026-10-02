export const SCENE = Object.freeze({
  limits: Object.freeze({ maxDevicePixelRatio: 1.5, maxBufferPixels: 1_000_000, maxDrawCalls: 30, maxTriangles: 5_000,
    settleMilliseconds: 500, settleRenderMilliseconds: 450, maxSpinStepMilliseconds: 50 }),
  world: Object.freeze({ ringRadius: 3.25, cardWidth: 1.2, cardHeight: 1.6, backOffset: 0.004, ringY: -0.25,
    ringTilt: 0, spinSpeed: 0.14, coreSize: 1.15, coreY: 0.6, shadowWidth: 8.4, shadowDepth: 3.8, shadowY: -1.2,
    cameraFov: 24, cameraNear: 0.1, cameraFar: 40, cameraX: 0, cameraY: 2.5, cameraZ: 11.2, cameraLookY: -0.32,
    cameraAspect: 1, pointerYaw: 0.12, pointerPitch: 0.05, smoothingDivisor: 115, fogNear: 10.5, fogFar: 16.5,
    fullTurn: Math.PI * 2, halfTurn: Math.PI / 2, millisecondsPerSecond: 1000 }),
  canvas: Object.freeze({ element: 'canvas', context: '2d', cardWidth: 512, cardHeight: 682, coreSize: 512,
    shadowSize: 256, radius: 56, inset: 3, border: 3, glyphX: 56, glyphY: 72, glyphSize: 132, stroke: 9,
    labelX: 56, labelY: 600, labelSize: 52, captionY: 640, captionSize: 22, sheenStop: 0.45,
    fontFamily: '-apple-system, BlinkMacSystemFont, "SF Pro Display", "Segoe UI", Inter, sans-serif',
    monoFamily: 'ui-monospace, "SF Mono", Menlo, Consolas, monospace', bold: '700 ', medium: '600 ', px: 'px ',
    round: 'round', ink: '#111214', soft: 'rgba(17, 18, 20, 0.55)', white: '#ffffff', sheen: 'rgba(255, 255, 255, 0.55)',
    clear: 'rgba(255, 255, 255, 0)', borderColor: 'rgba(255, 255, 255, 0.85)', shadow: 'rgba(17, 18, 20, 0.16)',
    graphite: '#16171a', graphiteLight: '#3a3b40', caption: 'KeyLoad' }),
  cards: Object.freeze([
    Object.freeze({ label: 'SQL', from: '#ffd2bd', to: '#f6c6ff', glyph: 'sql' }),
    Object.freeze({ label: 'Documents', from: '#f6c6ff', to: '#d8ccff', glyph: 'documents' }),
    Object.freeze({ label: 'Graphs', from: '#d8ccff', to: '#c4d3ff', glyph: 'graph' }),
    Object.freeze({ label: 'Events', from: '#c4d3ff', to: '#d9e9ff', glyph: 'events' }),
    Object.freeze({ label: 'Vectors', from: '#e2dcff', to: '#ffd6c6', glyph: 'vectors' }),
    Object.freeze({ label: 'Queues', from: '#ffd6c6', to: '#ffe7d6', glyph: 'queues' }),
    Object.freeze({ label: 'Time series', from: '#ffe2f2', to: '#e3d4ff', glyph: 'series' }),
    Object.freeze({ label: 'Blobs', from: '#dcd2ff', to: '#ffd9cc', glyph: 'blobs' }),
  ]),
  glyphs: Object.freeze({
    sql: Object.freeze({ text: 'SELECT', lines: [], circles: [], boxes: [] }),
    documents: Object.freeze({ lines: [[0.18, 0, 0.7, 0, 0.92, 0.22, 0.92, 1, 0.18, 1, 0.18, 0], [0.34, 0.42, 0.76, 0.42],
      [0.34, 0.62, 0.76, 0.62], [0.34, 0.82, 0.6, 0.82]], circles: [], boxes: [] }),
    graph: Object.freeze({ lines: [[0.16, 0.22, 0.82, 0.3], [0.16, 0.22, 0.46, 0.84], [0.82, 0.3, 0.46, 0.84]],
      circles: [[0.16, 0.22, 0.11], [0.82, 0.3, 0.11], [0.46, 0.84, 0.11]], boxes: [] }),
    events: Object.freeze({ lines: [[0, 0.5, 1, 0.5]], circles: [[0.1, 0.5, 0.08], [0.38, 0.5, 0.08], [0.66, 0.5, 0.08],
      [0.92, 0.5, 0.08]], boxes: [] }),
    vectors: Object.freeze({ lines: [[0.2, 0.86, 0.9, 0.52], [0.2, 0.86, 0.5, 0.1], [0.2, 0.86, 0.86, 0.9],
      [0.9, 0.52, 0.76, 0.44], [0.5, 0.1, 0.44, 0.24]], circles: [], boxes: [] }),
    queues: Object.freeze({ lines: [[0.74, 0.5, 1, 0.5], [0.88, 0.38, 1, 0.5, 0.88, 0.62]], circles: [],
      boxes: [[0, 0.1, 0.62, 0.2], [0, 0.4, 0.62, 0.2], [0, 0.7, 0.62, 0.2]] }),
    series: Object.freeze({ lines: [[0, 0.8, 0.2, 0.55, 0.38, 0.68, 0.58, 0.24, 0.78, 0.42, 1, 0.1]], circles: [], boxes: [] }),
    blobs: Object.freeze({ lines: [], circles: [], boxes: [[0, 0, 0.44, 0.44], [0.56, 0, 0.44, 0.44], [0, 0.56, 0.44, 0.44],
      [0.56, 0.56, 0.44, 0.44]] }),
  }),
  core: Object.freeze({ blocks: Object.freeze([[10, 9], [10, 17], [10, 25]]), blockWidth: 5, blockHeight: 6, blockRadius: 2,
    chevron: Object.freeze([29, 9.5, 19.5, 20, 29, 30.5]), chevronWidth: 4.4, viewBox: 40, tileRadius: 10,
    iris: Object.freeze(['#ffb08a', '#e3a6ff', '#9fb4ff']) }),
  colors: Object.freeze({ sky: 0xf2f1f2 }),
  math: Object.freeze({ zero: 0, half: 0.5, one: 1, two: 2, negativeOne: -1, epsilon: 0.001 }),
  state: Object.freeze({ poster: 'poster', loading: 'loading', ready: 'ready', paused: 'paused', zeroSize: 'zero-size',
    error: 'error', deviceLost: 'device-lost', unsupported: 'unsupported', disposed: 'disposed' }),
  frame: Object.freeze({ idle: 'idle', requested: 'requested', rendered: 'rendered', paused: 'paused', error: 'error' }),
  backend: Object.freeze({ webgpu: 'webgpu', webgl: 'webgl2', unknown: 'unknown' }),
  renderer: Object.freeze({ powerPreference: 'low-power', alpha: true, antialias: true }),
  types: Object.freeze({ function: 'function' }),
  visibility: Object.freeze({ visible: 'visible' }),
  dataset: Object.freeze({ state: 'sceneState', backend: 'graphicsBackend', pixels: 'bufferPixels', calls: 'drawCalls',
    renderCalls: 'renderCalls', triangles: 'triangles', frame: 'frameState' }),
  events: Object.freeze({ pointerMove: 'pointermove', click: 'click', resize: 'resize', pageHide: 'pagehide',
    pageShow: 'pageshow', visibility: 'visibilitychange', mediaChange: 'change' }),
  classes: Object.freeze({ host: 'cluster-scene-host', canvas: 'cluster-scene-canvas' }),
  attributes: Object.freeze({ ariaHidden: 'aria-hidden', ariaPressed: 'aria-pressed' }),
  observers: Object.freeze({ rootMargin: '80px', eventOptions: Object.freeze({ passive: true }) }),
  media: Object.freeze({ reducedMotion: '(prefers-reduced-motion: reduce)', coarsePointer: '(pointer: coarse)' }),
});

export const SCENE_TEXT = Object.freeze({
  poster: 'Static illustration.',
  loading: 'Loading the illustration.',
  ready: 'Conceptual illustration · not live data.',
  paused: 'Illustration paused.',
  zeroSize: 'The illustration is waiting for visible space.',
  error: 'The 3D illustration is unavailable; the static illustration remains visible.',
  deviceLost: 'Graphics were lost; the static illustration remains visible.',
  unsupported: 'This browser cannot start the 3D illustration.',
  disposed: 'Illustration stopped.',
  missingHost: 'A cluster scene host element is required.',
  enableMotion: 'Play motion',
  disableMotion: 'Pause motion',
  motionUnavailable: 'Motion off',
});

export function createSceneGraph(THREE) {
  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(SCENE.world.cameraFov, SCENE.world.cameraAspect,
    SCENE.world.cameraNear, SCENE.world.cameraFar);
  const root = new THREE.Group();
  const resources = new Set();
  const own = value => (resources.add(value), value);
  scene.fog = new THREE.Fog(SCENE.colors.sky, SCENE.world.fogNear, SCENE.world.fogFar);
  scene.add(root);
  root.add(shadowMesh(THREE, own));
  const ring = new THREE.Group();
  ring.position.y = SCENE.world.ringY;
  ring.rotation.x = SCENE.world.ringTilt;
  root.add(ring);
  const geometry = cardShape(THREE, own);
  SCENE.cards.forEach((card, index) => ring.add(cardMesh(THREE, own, geometry, card, index)));
  root.add(coreMesh(THREE, own));
  camera.position.set(SCENE.world.cameraX, SCENE.world.cameraY, SCENE.world.cameraZ);
  camera.lookAt(SCENE.math.zero, SCENE.world.cameraLookY, SCENE.math.zero);
  return {
    scene, camera, root,
    animate: seconds => { ring.rotation.y = seconds * SCENE.world.spinSpeed; },
    dispose: () => disposeGraph(resources),
  };
}

function surface(width, height) {
  const canvas = document.createElement(SCENE.canvas.element);
  canvas.width = width;
  canvas.height = height;
  return { canvas, context: canvas.getContext(SCENE.canvas.context) };
}

function texture(THREE, own, canvas) {
  const value = own(new THREE.CanvasTexture(canvas));
  value.colorSpace = THREE.SRGBColorSpace;
  return value;
}

function roundedPath(context, x, y, width, height, radius) {
  context.beginPath();
  context.roundRect(x, y, width, height, radius);
}

function paintCard(card, withLabel) {
  const { canvas, context } = surface(SCENE.canvas.cardWidth, SCENE.canvas.cardHeight);
  const inset = SCENE.canvas.inset;
  roundedPath(context, inset, inset, canvas.width - inset * SCENE.math.two, canvas.height - inset * SCENE.math.two,
    SCENE.canvas.radius);
  const fill = context.createLinearGradient(SCENE.math.zero, SCENE.math.zero, canvas.width, canvas.height);
  fill.addColorStop(SCENE.math.zero, card.from);
  fill.addColorStop(SCENE.math.one, card.to);
  context.fillStyle = fill;
  context.fill();
  const sheen = context.createLinearGradient(SCENE.math.zero, SCENE.math.zero, SCENE.math.zero, canvas.height);
  sheen.addColorStop(SCENE.math.zero, SCENE.canvas.sheen);
  sheen.addColorStop(SCENE.canvas.sheenStop, SCENE.canvas.clear);
  context.fillStyle = sheen;
  context.fill();
  context.lineWidth = SCENE.canvas.border;
  context.strokeStyle = SCENE.canvas.borderColor;
  context.stroke();
  if (withLabel) paintLabel(context, card);
  return canvas;
}

function paintLabel(context, card) {
  context.fillStyle = SCENE.canvas.ink;
  context.font = `${SCENE.canvas.bold}${SCENE.canvas.labelSize}${SCENE.canvas.px}${SCENE.canvas.fontFamily}`;
  context.fillText(card.label, SCENE.canvas.labelX, SCENE.canvas.labelY);
  context.fillStyle = SCENE.canvas.soft;
  context.font = `${SCENE.canvas.medium}${SCENE.canvas.captionSize}${SCENE.canvas.px}${SCENE.canvas.fontFamily}`;
  context.fillText(SCENE.canvas.caption, SCENE.canvas.labelX, SCENE.canvas.captionY);
  paintGlyph(context, SCENE.glyphs[card.glyph]);
}

function paintGlyph(context, glyph) {
  const size = SCENE.canvas.glyphSize;
  const point = (x, y) => [SCENE.canvas.glyphX + x * size, SCENE.canvas.glyphY + y * size];
  context.strokeStyle = SCENE.canvas.ink;
  context.fillStyle = SCENE.canvas.ink;
  context.lineWidth = SCENE.canvas.stroke;
  context.lineCap = SCENE.canvas.round;
  context.lineJoin = SCENE.canvas.round;
  if (glyph.text) {
    context.font = `${SCENE.canvas.bold}${size / SCENE.math.two}${SCENE.canvas.px}${SCENE.canvas.monoFamily}`;
    context.fillText(glyph.text, SCENE.canvas.glyphX, SCENE.canvas.glyphY + size * SCENE.math.half);
  }
  for (const line of glyph.lines) {
    context.beginPath();
    for (let index = SCENE.math.zero; index < line.length; index += SCENE.math.two) {
      const [x, y] = point(line[index], line[index + SCENE.math.one]);
      if (index) context.lineTo(x, y); else context.moveTo(x, y);
    }
    context.stroke();
  }
  for (const [x, y, radius] of glyph.circles) {
    const [cx, cy] = point(x, y);
    context.beginPath();
    context.arc(cx, cy, radius * size, SCENE.math.zero, SCENE.world.fullTurn);
    context.fill();
  }
  for (const [x, y, width, height] of glyph.boxes) {
    const [left, top] = point(x, y);
    roundedPath(context, left, top, width * size, height * size, SCENE.canvas.stroke);
    context.stroke();
  }
}

function cardShape(THREE, own) {
  return own(new THREE.PlaneGeometry(SCENE.world.cardWidth, SCENE.world.cardHeight));
}

function cardMesh(THREE, own, geometry, card, index) {
  const group = new THREE.Group();
  const angle = index / SCENE.cards.length * SCENE.world.fullTurn;
  group.position.set(Math.sin(angle) * SCENE.world.ringRadius, SCENE.math.zero, Math.cos(angle) * SCENE.world.ringRadius);
  group.rotation.y = angle;
  const front = new THREE.Mesh(geometry, own(new THREE.MeshBasicNodeMaterial({
    map: texture(THREE, own, paintCard(card, true)), transparent: true })));
  const back = new THREE.Mesh(geometry, own(new THREE.MeshBasicNodeMaterial({
    map: texture(THREE, own, paintCard(card, false)), transparent: true })));
  back.rotation.y = Math.PI;
  back.position.z = -SCENE.world.backOffset;
  group.add(front, back);
  return group;
}

function paintCore() {
  const { canvas, context } = surface(SCENE.canvas.coreSize, SCENE.canvas.coreSize);
  const scale = canvas.width / SCENE.core.viewBox;
  roundedPath(context, SCENE.math.zero, SCENE.math.zero, canvas.width, canvas.height, SCENE.core.tileRadius * scale);
  const fill = context.createLinearGradient(SCENE.math.zero, SCENE.math.zero, SCENE.math.zero, canvas.height);
  fill.addColorStop(SCENE.math.zero, SCENE.canvas.graphiteLight);
  fill.addColorStop(SCENE.math.one, SCENE.canvas.graphite);
  context.fillStyle = fill;
  context.fill();
  const iris = context.createLinearGradient(SCENE.math.zero, SCENE.math.zero, canvas.width, SCENE.math.zero);
  SCENE.core.iris.forEach((color, index) => iris.addColorStop(index / (SCENE.core.iris.length - SCENE.math.one), color));
  SCENE.core.blocks.forEach(([x, y], index) => {
    context.fillStyle = index === SCENE.core.blocks.length - SCENE.math.one ? iris : SCENE.canvas.white;
    roundedPath(context, x * scale, y * scale, SCENE.core.blockWidth * scale, SCENE.core.blockHeight * scale,
      SCENE.core.blockRadius * scale);
    context.fill();
  });
  const [x1, y1, x2, y2, x3, y3] = SCENE.core.chevron;
  context.strokeStyle = SCENE.canvas.white;
  context.lineWidth = SCENE.core.chevronWidth * scale;
  context.lineCap = SCENE.canvas.round;
  context.lineJoin = SCENE.canvas.round;
  context.beginPath();
  context.moveTo(x1 * scale, y1 * scale);
  context.lineTo(x2 * scale, y2 * scale);
  context.lineTo(x3 * scale, y3 * scale);
  context.stroke();
  return canvas;
}

function coreMesh(THREE, own) {
  const mesh = new THREE.Mesh(own(new THREE.PlaneGeometry(SCENE.world.coreSize, SCENE.world.coreSize)),
    own(new THREE.MeshBasicNodeMaterial({ map: texture(THREE, own, paintCore()), transparent: true, fog: false })));
  mesh.position.y = SCENE.world.coreY;
  return mesh;
}

function shadowMesh(THREE, own) {
  const { canvas, context } = surface(SCENE.canvas.shadowSize, SCENE.canvas.shadowSize);
  const middle = canvas.width * SCENE.math.half;
  const gradient = context.createRadialGradient(middle, middle, SCENE.math.zero, middle, middle, middle);
  gradient.addColorStop(SCENE.math.zero, SCENE.canvas.shadow);
  gradient.addColorStop(SCENE.math.one, SCENE.canvas.clear);
  context.fillStyle = gradient;
  context.fillRect(SCENE.math.zero, SCENE.math.zero, canvas.width, canvas.height);
  const mesh = new THREE.Mesh(own(new THREE.PlaneGeometry(SCENE.world.shadowWidth, SCENE.world.shadowDepth)),
    own(new THREE.MeshBasicNodeMaterial({ map: texture(THREE, own, canvas), transparent: true, depthWrite: false })));
  mesh.rotation.x = -SCENE.world.halfTurn;
  mesh.position.y = SCENE.world.shadowY;
  return mesh;
}

function disposeGraph(resources) {
  for (const value of resources) value.dispose();
  resources.clear();
}
