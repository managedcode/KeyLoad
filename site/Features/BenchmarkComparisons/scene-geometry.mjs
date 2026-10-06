export const SCENE = Object.freeze({
  limits: Object.freeze({ maxDevicePixelRatio: 1.5, maxBufferPixels: 1_000_000, maxDrawCalls: 30, maxTriangles: 5_000,
    settleMilliseconds: 500, settleRenderMilliseconds: 450, maxSpinStepMilliseconds: 50 }),
  world: Object.freeze({ coreSize: 1.05, coreY: 1.9, cameraFov: 24, cameraNear: 0.1, cameraFar: 60,
    cameraX: 0, cameraY: 2.5, cameraZ: 11.2, cameraLookY: 0.15, cameraAspect: 1,
    pointerYaw: 0.12, pointerPitch: 0.05, smoothingDivisor: 115, millisecondsPerSecond: 1000 }),
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

const SILOS = Object.freeze([[-2.35, -0.45, 0.6], [2.35, -0.45, 0.6], [0, -0.1, -2.1]]);
const GRAINS = Object.freeze([[-0.55, 0.45, 0], [-0.18, 0.82, -0.22], [0.52, 0.55, -0.1],
  [-0.42, 1.14, 0.05], [0.36, 1.3, -0.26], [0.1, 0.32, 0.33]]);
const EDGES = Object.freeze([[0, 1], [1, 2], [2, 5], [5, 0], [0, 3], [3, 1], [1, 4], [4, 2], [3, 4]]);
const COLORS = Object.freeze([0xeac8bd, 0xc7c5e7, 0xd2c1df]);

export function createSceneGraph(THREE) {
  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(SCENE.world.cameraFov, 1, SCENE.world.cameraNear, SCENE.world.cameraFar);
  const root = new THREE.Group();
  const resources = new Set();
  const own = value => (resources.add(value), value);
  scene.add(root, new THREE.AmbientLight(0xffffff, 2));
  const light = new THREE.DirectionalLight(0xffffff, 3);
  light.position.set(-3, 6, 5);
  scene.add(light);
  const nodes = SILOS.map((position, index) => createSilo(THREE, own, root, position, COLORS[index]));
  const positions = SILOS.flatMap(silo => GRAINS.map(grain => new THREE.Vector3(...grain).add(new THREE.Vector3(...silo))));
  const grains = new THREE.InstancedMesh(own(new THREE.SphereGeometry(0.12, 12, 8)),
    own(new THREE.MeshStandardNodeMaterial({ roughness: 0.3, metalness: 0.12 })), positions.length);
  own(grains);
  const matrix = new THREE.Matrix4();
  positions.forEach((position, index) => {
    grains.setMatrixAt(index, matrix.makeTranslation(position.x, position.y, position.z));
    grains.setColorAt(index, new THREE.Color(COLORS[Math.floor(index / GRAINS.length)]));
  });
  root.add(grains);
  const links = SILOS.flatMap((_, silo) => EDGES.map(([a, b]) => [silo * GRAINS.length + a, silo * GRAINS.length + b]));
  links.push([1, 13], [7, 16], [5, 11]);
  const vertices = links.flatMap(([a, b]) => [...positions[a].toArray(), ...positions[b].toArray()]);
  const geometry = own(new THREE.BufferGeometry());
  geometry.setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3));
  const lineMaterial = own(new THREE.LineBasicNodeMaterial({ color: 0x777486, transparent: true, opacity: 0.48 }));
  root.add(new THREE.LineSegments(geometry, lineMaterial));
  const projectLabels = createLabelProjection(THREE, root, camera);
  const projectCore = createCoreProjection(THREE, root, camera);
  const resize = aspect => {
    camera.aspect = aspect;
    camera.position.set(0, SCENE.world.cameraY, SCENE.world.cameraZ * Math.max(1, 1.65 / aspect));
    camera.lookAt(0, SCENE.world.cameraLookY, 0);
    camera.updateProjectionMatrix();
  };
  resize(1);
  return {
    scene, camera, root, projectCore, projectLabels, resize,
    counts: { silos: nodes.length, grains: positions.length, links: links.length },
    animate: seconds => {
      positions.forEach((position, index) => {
        const scale = 1 + Math.sin(seconds * 1.7 + index * 0.9) * 0.12;
        matrix.makeScale(scale, scale, scale).setPosition(position);
        grains.setMatrixAt(index, matrix);
      });
      grains.instanceMatrix.needsUpdate = true;
      lineMaterial.opacity = 0.42 + Math.sin(seconds * 0.7) * 0.06;
    },
    dispose: () => { for (const value of resources) value.dispose(); resources.clear(); },
  };
}

function createSilo(THREE, own, root, position, color) {
  const group = new THREE.Group();
  group.position.set(...position);
  const glass = new THREE.Mesh(own(new THREE.BoxGeometry(1.7, 1.65, 0.95)),
    own(new THREE.MeshBasicNodeMaterial({ color, transparent: true, opacity: 0.13, depthWrite: false })));
  glass.position.y = 0.82;
  const outline = new THREE.LineSegments(own(new THREE.EdgesGeometry(glass.geometry)),
    own(new THREE.LineBasicNodeMaterial({ color, transparent: true, opacity: 0.55 })));
  outline.position.copy(glass.position);
  const base = new THREE.Mesh(own(new THREE.BoxGeometry(1.85, 0.12, 1.1)),
    own(new THREE.MeshStandardNodeMaterial({ color: 0x34343b, roughness: 0.6, metalness: 0.2 })));
  group.add(glass, outline, base);
  root.add(group);
  return group;
}

function createLabelProjection(THREE, root, camera) {
  const anchors = SILOS.map(position => {
    const anchor = new THREE.Object3D();
    anchor.position.set(position[0], position[1] - 0.36, position[2] + 0.4);
    root.add(anchor);
    return anchor;
  });
  const point = new THREE.Vector3();
  const result = anchors.map(() => ({ x: 0, y: 0 }));
  return (width, height) => {
    anchors.forEach((anchor, index) => {
      anchor.getWorldPosition(point).project(camera);
      result[index].x = (point.x + 1) * width / 2;
      result[index].y = (1 - point.y) * height / 2;
    });
    return result;
  };
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
