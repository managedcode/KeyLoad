export const SCENE = Object.freeze({
  limits: Object.freeze({ maxDevicePixelRatio: 1.5, maxBufferPixels: 1_000_000, maxDrawCalls: 30, maxTriangles: 5_000,
    settleMilliseconds: 500, settleRenderMilliseconds: 450 }),
  world: Object.freeze({ cubeSize: 3, cubeY: 1.75, edgeScale: 1.001, graphY: 2.55, graphSpread: 0.95,
    nodeRadius: 0.085, documentY: 0.62, documentWidth: 0.62, documentHeight: 0.028, documentDepth: 0.44,
    documentGap: 0.07, documentStacks: Object.freeze([[-0.72, 0.62, 5], [-0.05, 0.75, 4], [0.66, 0.5, 3]]),
    eventCount: 18, eventRadius: 0.055, eventArc: 1.15, eventY: 1.62, vectorCount: 110, vectorSize: 0.04,
    vectorCenterX: 0.72, vectorCenterY: 1.35, vectorCenterZ: -0.62, vectorSpread: 0.42, seriesY: 1.95,
    seriesPoints: 32, seriesWidth: 2.4, seriesAmplitude: 0.24, seriesZ: 1.05, agentRadius: 0.1,
    agentPositions: Object.freeze([[-3.1, 3.3, 0.6], [3.2, 2.7, 1.4], [-1.6, 0.9, 3.1]]), seed: 7,
    gridExtent: 9, gridStep: 0.9, gridY: 0, cameraFov: 34, cameraNear: 0.1, cameraFar: 60, cameraX: 4.2,
    cameraY: 3.4, cameraZ: 7.4, cameraLookY: 1.6, cameraAspect: 1, pointerYaw: 0.1, pointerPitch: 0.04,
    smoothingDivisor: 115, fogNear: 9, fogFar: 22, graphNodes: 7, graphInner: 0.55, graphJitter: 0.35,
    graphChord: 3, documentLift: 0.38, eventSweep: 0.9, eventRise: 0.18, eventDepth: 0.55, eventOffset: 0.2,
    vectorHeight: 1.6, seriesSlow: 9, seriesFast: 23, seriesSlowWeight: 0.5, seriesFastWeight: 0.25,
    seriesTrend: 0.6, fullTurn: Math.PI * 2, directionalX: 4, directionalY: 8,
    directionalZ: 6, randomModulus: 2147483647, randomMultiplier: 48271 }),
  material: Object.freeze({ nodeRoughness: 0.35, documentRoughness: 0.6, eventRoughness: 0.4,
    agentRoughness: 0.3, glassOpacity: 0.045, edgeOpacity: 0.38, lineOpacity: 0.5, gridOpacity: 0.5,
    agentGlow: 1.4, beamOpacity: 0.5, hemisphereSky: 0xffffff, hemisphereGround: 0x1a1c20,
    hemisphereIntensity: 1.3, directionalColor: 0xffffff, directionalIntensity: 2.3 }),
  math: Object.freeze({ zero: 0, half: 0.5, one: 1, two: 2, negativeOne: -1, epsilon: 0.001 }),
  colors: Object.freeze({ sky: 0x0c0d10, glass: 0xffffff, edge: 0xffffff, lime: 0xc6f24e, node: 0xd9cbff,
    document: 0xf2f2f4, event: 0xffb08a, vector: 0xb9a6ff, grid: 0x23262c, line: 0xffffff }),
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
  attributes: Object.freeze({ ariaHidden: 'aria-hidden', ariaPressed: 'aria-pressed', position: 'position' }),
  observers: Object.freeze({ rootMargin: '80px', eventOptions: Object.freeze({ passive: true }) }),
  media: Object.freeze({ reducedMotion: '(prefers-reduced-motion: reduce)', coarsePointer: '(pointer: coarse)' }),
});

export const SCENE_TEXT = Object.freeze({
  poster: 'Static illustration.',
  loading: 'Loading a conceptual cluster illustration.',
  ready: 'Conceptual illustration · not live data.',
  paused: 'Conceptual cluster illustration paused.',
  zeroSize: 'Conceptual cluster illustration is waiting for visible space.',
  error: 'The conceptual illustration is unavailable; the static illustration remains visible.',
  deviceLost: 'Graphics were lost; the static illustration remains visible.',
  unsupported: 'This browser cannot initialize the conceptual illustration.',
  disposed: 'Conceptual illustration stopped.',
  missingHost: 'A cluster scene host element is required.',
  enableMotion: 'Enable motion',
  disableMotion: 'Disable motion',
  motionUnavailable: 'Motion unavailable',
});

export function createSceneGraph(THREE) {
  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(SCENE.world.cameraFov, SCENE.world.cameraAspect,
    SCENE.world.cameraNear, SCENE.world.cameraFar);
  const root = new THREE.Group();
  const geometries = new Set();
  const materials = new Set();
  const own = { geometry: (value) => (geometries.add(value), value), material: (value) => (materials.add(value), value),
    random: seededRandom(SCENE.world.seed) };
  scene.background = new THREE.Color(SCENE.colors.sky);
  scene.fog = new THREE.Fog(SCENE.colors.sky, SCENE.world.fogNear, SCENE.world.fogFar);
  addLights(THREE, scene);
  scene.add(root);
  addFloor(THREE, root, own);
  addGraph(THREE, root, own);
  addDocuments(THREE, root, own);
  addEvents(THREE, root, own);
  addVectors(THREE, root, own);
  addSeries(THREE, root, own);
  addAgents(THREE, root, own);
  addEngine(THREE, root, own);
  camera.position.set(SCENE.world.cameraX, SCENE.world.cameraY, SCENE.world.cameraZ);
  camera.lookAt(SCENE.math.zero, SCENE.world.cameraLookY, SCENE.math.zero);
  return { scene, camera, root, dispose: () => disposeGraph(geometries, materials) };
}

function seededRandom(seed) {
  let value = seed;
  return () => {
    value = value * SCENE.world.randomMultiplier % SCENE.world.randomModulus;
    return value / SCENE.world.randomModulus;
  };
}

function addLights(THREE, scene) {
  scene.add(new THREE.HemisphereLight(SCENE.material.hemisphereSky, SCENE.material.hemisphereGround,
    SCENE.material.hemisphereIntensity));
  const sun = new THREE.DirectionalLight(SCENE.material.directionalColor, SCENE.material.directionalIntensity);
  sun.position.set(SCENE.world.directionalX, SCENE.world.directionalY, SCENE.world.directionalZ);
  scene.add(sun);
}

function lines(THREE, own, points, color, opacity) {
  return new THREE.LineSegments(own.geometry(new THREE.BufferGeometry().setFromPoints(points)),
    own.material(new THREE.LineBasicNodeMaterial({ color, transparent: true, opacity })));
}

function instances(THREE, own, geometry, material, positions, scale = SCENE.math.one) {
  const mesh = new THREE.InstancedMesh(own.geometry(geometry), own.material(material), positions.length);
  const placement = new THREE.Object3D();
  positions.forEach((position, index) => {
    placement.position.copy(position);
    placement.scale.setScalar(scale);
    placement.updateMatrix();
    mesh.setMatrixAt(index, placement.matrix);
  });
  return mesh;
}

function addEngine(THREE, root, own) {
  const shape = own.geometry(new THREE.BoxGeometry(SCENE.world.cubeSize, SCENE.world.cubeSize, SCENE.world.cubeSize));
  const glass = new THREE.Mesh(shape, own.material(new THREE.MeshBasicNodeMaterial({ color: SCENE.colors.glass,
    transparent: true, opacity: SCENE.material.glassOpacity, depthWrite: false })));
  glass.position.y = SCENE.world.cubeY;
  const edges = lines(THREE, own, edgePoints(THREE, shape), SCENE.colors.edge, SCENE.material.edgeOpacity);
  edges.scale.setScalar(SCENE.world.edgeScale);
  glass.add(edges);
  root.add(glass);
}

function edgePoints(THREE, shape) {
  const edges = new THREE.EdgesGeometry(shape);
  const attribute = edges.getAttribute(SCENE.attributes.position);
  const points = [];
  for (let index = SCENE.math.zero; index < attribute.count; index += SCENE.math.one)
    points.push(new THREE.Vector3().fromBufferAttribute(attribute, index));
  edges.dispose();
  return points;
}

function addGraph(THREE, root, own) {
  const nodes = [];
  for (let index = SCENE.math.zero; index < SCENE.world.graphNodes; index += SCENE.math.one) {
    const angle = index / SCENE.world.graphNodes * SCENE.world.fullTurn;
    const radius = SCENE.world.graphSpread * (index % SCENE.math.two ? SCENE.world.graphInner : SCENE.math.one);
    nodes.push(new THREE.Vector3(Math.cos(angle) * radius, SCENE.world.graphY + (own.random() - SCENE.math.half) * SCENE.world.graphJitter,
      Math.sin(angle) * radius));
  }
  const links = [];
  nodes.forEach((node, index) => links.push(node, nodes[(index + SCENE.math.one) % nodes.length], node, nodes[(index + SCENE.world.graphChord) % nodes.length]));
  root.add(lines(THREE, own, links, SCENE.colors.line, SCENE.material.lineOpacity));
  root.add(instances(THREE, own, new THREE.IcosahedronGeometry(SCENE.world.nodeRadius, SCENE.math.one),
    new THREE.MeshStandardNodeMaterial({ color: SCENE.colors.node, roughness: SCENE.material.nodeRoughness }), nodes));
}

function addDocuments(THREE, root, own) {
  const positions = [];
  for (const [x, z, count] of SCENE.world.documentStacks)
    for (let level = SCENE.math.zero; level < count; level += SCENE.math.one)
      positions.push(new THREE.Vector3(x, SCENE.world.documentY + level * SCENE.world.documentGap - SCENE.world.documentLift, z));
  root.add(instances(THREE, own, new THREE.BoxGeometry(SCENE.world.documentWidth, SCENE.world.documentHeight,
    SCENE.world.documentDepth), new THREE.MeshStandardNodeMaterial({ color: SCENE.colors.document, roughness: SCENE.material.documentRoughness }), positions));
}

function addEvents(THREE, root, own) {
  const positions = [];
  for (let index = SCENE.math.zero; index < SCENE.world.eventCount; index += SCENE.math.one) {
    const t = index / (SCENE.world.eventCount - SCENE.math.one);
    const angle = (t - SCENE.math.half) * Math.PI * SCENE.world.eventSweep;
    positions.push(new THREE.Vector3(Math.sin(angle) * SCENE.world.eventArc, SCENE.world.eventY + t * SCENE.world.eventRise,
      Math.cos(angle) * SCENE.world.eventArc * SCENE.world.eventDepth - SCENE.world.eventOffset));
  }
  root.add(instances(THREE, own, new THREE.IcosahedronGeometry(SCENE.world.eventRadius),
    new THREE.MeshStandardNodeMaterial({ color: SCENE.colors.event, roughness: SCENE.material.eventRoughness }), positions));
}

function addVectors(THREE, root, own) {
  const positions = [];
  for (let index = SCENE.math.zero; index < SCENE.world.vectorCount; index += SCENE.math.one)
    positions.push(new THREE.Vector3(SCENE.world.vectorCenterX + (own.random() - SCENE.math.half) * SCENE.world.vectorSpread * SCENE.math.two,
      SCENE.world.vectorCenterY + (own.random() - SCENE.math.half) * SCENE.world.vectorSpread * SCENE.world.vectorHeight,
      SCENE.world.vectorCenterZ + (own.random() - SCENE.math.half) * SCENE.world.vectorSpread * SCENE.math.two));
  root.add(instances(THREE, own, new THREE.TetrahedronGeometry(SCENE.world.vectorSize),
    new THREE.MeshBasicNodeMaterial({ color: SCENE.colors.vector }), positions));
}

function addSeries(THREE, root, own) {
  const points = [];
  for (let index = SCENE.math.zero; index < SCENE.world.seriesPoints; index += SCENE.math.one) {
    const t = index / (SCENE.world.seriesPoints - SCENE.math.one);
    const wave = Math.sin(t * SCENE.world.seriesSlow) * SCENE.world.seriesSlowWeight
      + Math.sin(t * SCENE.world.seriesFast) * SCENE.world.seriesFastWeight + t * SCENE.world.seriesTrend;
    points.push(new THREE.Vector3((t - SCENE.math.half) * SCENE.world.seriesWidth,
      SCENE.world.seriesY + wave * SCENE.world.seriesAmplitude, SCENE.world.seriesZ));
  }
  root.add(new THREE.Line(own.geometry(new THREE.BufferGeometry().setFromPoints(points)),
    own.material(new THREE.LineBasicNodeMaterial({ color: SCENE.colors.lime }))));
}

function addAgents(THREE, root, own) {
  const agents = SCENE.world.agentPositions.map(([x, y, z]) => new THREE.Vector3(x, y, z));
  root.add(instances(THREE, own, new THREE.IcosahedronGeometry(SCENE.world.agentRadius, SCENE.math.two),
    new THREE.MeshStandardNodeMaterial({ color: SCENE.colors.lime, emissive: SCENE.colors.lime,
      emissiveIntensity: SCENE.material.agentGlow, roughness: SCENE.material.agentRoughness }), agents));
  const half = SCENE.world.cubeSize * SCENE.math.half;
  const center = new THREE.Vector3(SCENE.math.zero, SCENE.world.cubeY, SCENE.math.zero);
  const beams = agents.flatMap(agent => {
    const direction = center.clone().sub(agent);
    const scale = Math.max(Math.abs(direction.x), Math.abs(direction.y), Math.abs(direction.z));
    const surface = center.clone().sub(direction.clone().multiplyScalar(half / scale));
    return [agent, surface];
  });
  root.add(lines(THREE, own, beams, SCENE.colors.lime, SCENE.material.beamOpacity));

}

function addFloor(THREE, root, own) {
  const points = [];
  for (let offset = -SCENE.world.gridExtent; offset <= SCENE.world.gridExtent; offset += SCENE.world.gridStep) {
    points.push(new THREE.Vector3(offset, SCENE.world.gridY, -SCENE.world.gridExtent),
      new THREE.Vector3(offset, SCENE.world.gridY, SCENE.world.gridExtent),
      new THREE.Vector3(-SCENE.world.gridExtent, SCENE.world.gridY, offset),
      new THREE.Vector3(SCENE.world.gridExtent, SCENE.world.gridY, offset));
  }
  root.add(lines(THREE, own, points, SCENE.colors.grid, SCENE.material.gridOpacity));
}

function disposeGraph(geometries, materials) {
  for (const value of geometries) value.dispose();
  for (const value of materials) value.dispose();
  geometries.clear();
  materials.clear();
}
