export const SCENE = Object.freeze({
  limits: Object.freeze({ maxDevicePixelRatio: 1.5, maxBufferPixels: 1_000_000, maxDrawCalls: 30, maxTriangles: 5_000,
    settleMilliseconds: 500, settleRenderMilliseconds: 450, maxSpinStepMilliseconds: 50 }),
  world: Object.freeze({ ringRadius: 3.25, cardWidth: 1.2, cardHeight: 1.6, backOffset: 0.004, ringY: -0.25,
    ringTilt: 0, spinSpeed: 0.14, coreSize: 1.15, coreY: 0.6, shadowWidth: 8.4, shadowDepth: 3.8, shadowY: -1.2,
    cameraFov: 24, cameraNear: 0.1, cameraFar: 40, cameraX: 0, cameraY: 2.5, cameraZ: 11.2, cameraLookY: -0.32,
    cameraAspect: 1, pointerYaw: 0.12, pointerPitch: 0.05, smoothingDivisor: 115, fogNear: 10.5, fogFar: 16.5,
    fullTurn: Math.PI * 2, halfTurn: Math.PI / 2, millisecondsPerSecond: 1000 }),
  canvas: Object.freeze({ element: 'canvas', context: '2d', cardWidth: 512, cardHeight: 682,
    shadowSize: 256, radius: 56, inset: 3, border: 3, glyphX: 56, glyphY: 72, glyphSize: 132, stroke: 9,
    labelX: 56, labelY: 600, labelSize: 52, captionY: 640, captionSize: 22, sheenStop: 0.45,
    fontFamily: '-apple-system, BlinkMacSystemFont, "SF Pro Display", "Segoe UI", Inter, sans-serif',
    monoFamily: 'ui-monospace, "SF Mono", Menlo, Consolas, monospace', bold: '700 ', medium: '600 ', px: 'px ',
    round: 'round', ink: '#111214', soft: 'rgba(17, 18, 20, 0.55)', sheen: 'rgba(255, 255, 255, 0.55)',
    clear: 'rgba(255, 255, 255, 0)', borderColor: 'rgba(255, 255, 255, 0.85)', shadow: 'rgba(17, 18, 20, 0.16)',
    caption: 'KeyLoad' }),
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
  core: Object.freeze({ selector: '.cluster-core', perspectiveScaleIndex: 5, matrixPrefix: 'matrix3d(', matrixSuffix: ')',
    separator: ',', pixels: 'px' }),
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
  attributes: Object.freeze({ ariaHidden: 'aria-hidden', ariaPressed: 'aria-pressed', style: 'style' }),
  observers: Object.freeze({ rootMargin: '80px', eventOptions: Object.freeze({ passive: true }) }),
  media: Object.freeze({ reducedMotion: '(prefers-reduced-motion: reduce)', coarsePointer: '(pointer: coarse)' }),
});

export const SCENE_TEXT = Object.freeze({
  poster: 'Static illustration.',
  loading: 'Loading the illustration.',
  ready: 'Illustration running.',
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
  const projectCore = createCoreProjection(THREE, root, camera);
  camera.position.set(SCENE.world.cameraX, SCENE.world.cameraY, SCENE.world.cameraZ);
  camera.lookAt(SCENE.math.zero, SCENE.world.cameraLookY, SCENE.math.zero);
  return {
    scene, camera, root, projectCore,
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

function createCoreProjection(THREE, root, camera) {
  const anchor = new THREE.Object3D();
  anchor.position.y = SCENE.world.coreY;
  root.add(anchor);
  const center = new THREE.Vector3();
  const placement = new THREE.Matrix4();
  const projection = new THREE.Matrix4();
  const screen = new THREE.Matrix4();
  return (width, height) => {
    anchor.getWorldPosition(center).applyMatrix4(camera.matrixWorldInverse);
    const size = height * camera.projectionMatrix.elements[SCENE.core.perspectiveScaleIndex] * SCENE.world.coreSize
      / (-center.z * SCENE.math.two);
    const scale = SCENE.world.coreSize / size;
    placement.makeScale(scale, -scale, SCENE.math.one);
    placement.setPosition(-SCENE.world.coreSize * SCENE.math.half, SCENE.world.coreSize * SCENE.math.half, SCENE.math.zero);
    projection.multiplyMatrices(camera.projectionMatrix, camera.matrixWorldInverse).multiply(anchor.matrixWorld).multiply(placement);
    screen.set(width * SCENE.math.half, SCENE.math.zero, SCENE.math.zero, width * SCENE.math.half,
      SCENE.math.zero, -height * SCENE.math.half, SCENE.math.zero, height * SCENE.math.half,
      SCENE.math.zero, SCENE.math.zero, SCENE.math.one, SCENE.math.zero,
      SCENE.math.zero, SCENE.math.zero, SCENE.math.zero, SCENE.math.one);
    screen.multiply(projection);
    return { size, transform: SCENE.core.matrixPrefix + screen.elements.join(SCENE.core.separator) + SCENE.core.matrixSuffix };
  };
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
