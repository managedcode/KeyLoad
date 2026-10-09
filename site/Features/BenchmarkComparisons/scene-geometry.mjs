export const SCENE = Object.freeze({
  limits: Object.freeze({ maxDevicePixelRatio: 1.5, maxBufferPixels: 1_000_000, maxDrawCalls: 30, maxTriangles: 120_000,
    settleMilliseconds: 500, settleRenderMilliseconds: 450, maxSpinStepMilliseconds: 50 }),
  world: Object.freeze({ coreSize: 0.8, coreY: 2.0, cameraFov: 32, cameraNear: 0.1, cameraFar: 60,
    cameraX: 0, cameraY: 6, cameraZ: 11.5, cameraLookY: 0.15, cameraAspect: 1,
    pointerYaw: 0.065, pointerPitch: 0.025, smoothingDivisor: 115, millisecondsPerSecond: 1000 }),
  colors: Object.freeze({ sky: 0xf7f3ed }),
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

// Positions and proportions measured from the selected original product artwork.
const PLATFORMS = Object.freeze([
  [-2.55, -0.79, -1.75], [2.65, -0.79, -1.65], [-4, -0.79, 0.45], [4.05, -0.79, 0.5],
  [-3.25, -0.79, 2.15], [3.4, -0.79, 2.25], [-1.75, -0.79, 3.3], [1.85, -0.79, 3.35],
]);
const PALETTE = Object.freeze({ silver: 0xe4deda, edge: 0xc1b8b5, paper: 0xf5efe6,
  glass: 0xe7dfe9, lilac: 0xb4a3e4, peach: 0xf3bf9d, ink: 0xc0b4b1, white: 0xfff9f0 });
const CORE_ANCHOR = 8, AGENT_ANCHOR = 9;

export function createSceneGraph(THREE) {
  const scene = new THREE.Scene(), root = new THREE.Group(), resources = new Set();
  const own = value => (resources.add(value), value);
  const camera = new THREE.OrthographicCamera(-5.4, 5.4, 2.7, -2.7, 0.1, 60);
  camera.position.set(0, SCENE.world.cameraY, SCENE.world.cameraZ);
  camera.lookAt(0, 0.35, 0.65);
  scene.environment = createStudioEnvironment(THREE, own);
  scene.add(root, new THREE.AmbientLight(0xfff4e8, 1.2));
  const key = new THREE.DirectionalLight(0xfff8ed, 2.4);
  key.position.set(-4, 7, 5);
  const fill = new THREE.DirectionalLight(0xe2d9ff, 1.7);
  fill.position.set(4, 4, -3);
  scene.add(key, fill);
  const anchors = [...PLATFORMS.map(position => new THREE.Vector3(...position)),
    new THREE.Vector3(), new THREE.Vector3(0, 1.6, -0.08)];
  const shapes = createShapes(THREE);
  const surfaces = { metal: [], paper: [], glass: [], detail: [] };
  const poses = anchors.map((_, index) => new THREE.Quaternion().setFromEuler(new THREE.Euler(0,
    ({ 0: -0.15, 5: -0.18, 6: -0.15, 7: 0.14 })[index] ?? 0, 0)));
  const piece = (surface, shape, anchor, offset, size, color, angles = [0, 0, 0]) => {
    surfaces[surface].push({ geometry: shapes[shape], anchor,
      offset: new THREE.Vector3(...offset).applyQuaternion(poses[anchor]),
      size: new THREE.Vector3(...size), color,
      rotation: poses[anchor].clone().multiply(new THREE.Quaternion().setFromEuler(new THREE.Euler(...angles))) });
  };
  createPlatforms(piece);
  createDatabase(piece);
  createAgent(piece);
  createTable(piece);
  createGraph(THREE, piece, surfaces, shapes);
  createTimeSeries(THREE, piece, surfaces, shapes);
  createVectors(piece);
  createEvents(piece);
  createDocuments(piece);
  createQueue(piece);
  createFiles(piece);
  createContactShade(THREE, own, root);
  const materials = {
    metal: own(new THREE.MeshPhysicalNodeMaterial({ metalness: 0.86, roughness: 0.24,
      clearcoat: 1, iridescence: 0.32, vertexColors: true })),
    paper: own(new THREE.MeshPhysicalNodeMaterial({ metalness: 0.15, roughness: 0.3,
      clearcoat: 0.65, vertexColors: true })),
    glass: own(new THREE.MeshPhysicalNodeMaterial({ metalness: 0.25, roughness: 0.17,
      transparent: true, opacity: 0.36, depthWrite: false, clearcoat: 1, vertexColors: true })),
    detail: own(new THREE.MeshPhysicalNodeMaterial({ metalness: 0.36, roughness: 0.22,
      clearcoat: 1, iridescence: 0.45, vertexColors: true })),
  };
  materials.metal.roughnessNode = THREE.TSL.positionLocal.x.pow(2)
    .add(THREE.TSL.positionLocal.z.pow(2)).sqrt().mul(640).sin().mul(0.025).add(0.24);
  for (const [name, pieces] of Object.entries(surfaces)) {
    createSurface(THREE, own, root, anchors, pieces, materials[name]);
  }
  for (const geometry of new Set(Object.values(shapes))) geometry.dispose();
  const cables = createCables(THREE, own, root);
  const animate = seconds => {
    anchors[AGENT_ANCHOR].y = 1.6 + Math.sin(seconds * 0.8) * 0.018;
    cables.animate(seconds);
  };
  const resize = aspect => {
    // Keep the complete original composition in frame, including the front platforms.
    const halfHeight = Math.max(2.85, 5.4 / aspect);
    camera.aspect = aspect;
    camera.left = -halfHeight * aspect;
    camera.right = halfHeight * aspect;
    camera.top = halfHeight;
    camera.bottom = -halfHeight;
    camera.updateProjectionMatrix();
  };
  resize(2);
  animate(0);
  return { scene, camera, root, resize, animate,
    counts: { models: PLATFORMS.length, agents: 1, links: cables.count, clients: 0 },
    dispose: () => { for (const value of resources) value.dispose(); resources.clear(); },
  };
}

function createShapes(THREE) {
  const shape = new THREE.Shape(), radius = 0.08;
  shape.moveTo(-0.5 + radius, -0.5);
  shape.lineTo(0.5 - radius, -0.5);
  shape.quadraticCurveTo(0.5, -0.5, 0.5, -0.5 + radius);
  shape.lineTo(0.5, 0.5 - radius);
  shape.quadraticCurveTo(0.5, 0.5, 0.5 - radius, 0.5);
  shape.lineTo(-0.5 + radius, 0.5);
  shape.quadraticCurveTo(-0.5, 0.5, -0.5, 0.5 - radius);
  shape.lineTo(-0.5, -0.5 + radius);
  shape.quadraticCurveTo(-0.5, -0.5, -0.5 + radius, -0.5);
  const panel = new THREE.ExtrudeGeometry(shape, { depth: 1, bevelEnabled: true,
    bevelThickness: 0.08, bevelSize: 0.025, bevelSegments: 3, curveSegments: 6 });
  panel.translate(0, 0, -0.5);
  const profile = [[0, -0.5], [0.92, -0.5], [0.97, -0.46], [1, -0.36],
    [1, 0.36], [0.97, 0.46], [0.92, 0.5], [0, 0.5]];
  const body = [[0, 0], [0.18, 0], [0.3, 0.035], [0.35, 0.13], [0.34, 0.24],
    [0.29, 0.36], [0.19, 0.45], [0.07, 0.48], [0, 0.48]];
  return { panel, box: new THREE.BoxGeometry(1, 1, 1),
    disk: new THREE.LatheGeometry(profile.map(point => new THREE.Vector2(...point)), 96),
    sphere: new THREE.SphereGeometry(1, 24, 16), bead: new THREE.SphereGeometry(1, 10, 7),
    cylinder: new THREE.CylinderGeometry(1, 1, 1, 24),
    ring: new THREE.TorusGeometry(1, 0.015, 8, 96),
    orbit: new THREE.TorusGeometry(1, 0.011, 6, 80),
    body: new THREE.LatheGeometry(body.map(point => new THREE.Vector2(...point)), 64) };
}

function createPlatforms(piece) {
  for (let index = 0; index < PLATFORMS.length; index++) {
    if ([0, 2, 6].includes(index)) {
      const size = index === 2 ? [1.85, 1, 0.16] : [1.55, 1, 0.16];
      piece('metal', 'panel', index, [0, 0, 0], size, PALETTE.silver, [-Math.PI / 2, 0, 0]);
      piece('paper', 'panel', index, [0, 0.085, 0], [size[0] * 0.975, 0.965, 0.018],
        PALETTE.paper, [-Math.PI / 2, 0, 0]);
    } else {
      piece('metal', 'disk', index, [0, 0, 0], [0.87, 0.16, 0.7], PALETTE.silver);
      piece('paper', 'disk', index, [0, 0.085, 0], [0.845, 0.018, 0.677], PALETTE.paper);
    }
  }
}

function createDatabase(piece) {
  piece('metal', 'disk', CORE_ANCHOR, [0, -0.72, 0], [1.18, 0.16, 1.18], PALETTE.edge);
  for (let index = 0; index < 3; index++) {
    const y = -0.4 + index * 0.56;
    piece('metal', 'disk', CORE_ANCHOR, [0, y, 0], [1.1, 0.5, 1.1], PALETTE.silver);
    piece('metal', 'ring', CORE_ANCHOR, [0, y + 0.23, 0], [1.105, 1.105, 1.105], PALETTE.white,
      [-Math.PI / 2, 0, 0]);
    piece('detail', 'ring', CORE_ANCHOR, [0, y - 0.23, 0], [1.11, 1.11, 1.11],
      index === 0 ? PALETTE.peach : PALETTE.lilac, [-Math.PI / 2, 0, 0]);
  }
  // Eight fitted ports join the shared database; platform connections stay visible.
  for (let index = 0; index < PLATFORMS.length; index++) {
    const position = PLATFORMS[index], angle = Math.atan2(position[0], position[2]);
    const y = index < 2 ? 0.5 : index < 4 ? 0.06 : -0.38;
    piece('metal', 'cylinder', CORE_ANCHOR, [Math.sin(angle) * 1.12, y, Math.cos(angle) * 1.12],
      [0.088, 0.09, 0.088], PALETTE.edge, [Math.PI / 2, 0, -angle]);
  }
}

function createAgent(piece) {
  piece('glass', 'cylinder', CORE_ANCHOR, [0, 1.22, -0.08], [0.029, 0.54, 0.029], PALETTE.lilac);
  piece('detail', 'cylinder', CORE_ANCHOR, [0, 1.22, -0.08], [0.009, 0.54, 0.009], PALETTE.lilac);
  piece('metal', 'body', AGENT_ANCHOR, [0, -0.04, 0], [0.98, 0.98, 0.75], PALETTE.silver);
  piece('metal', 'sphere', AGENT_ANCHOR, [0, 0.6, 0], [0.19, 0.19, 0.19], PALETTE.silver);
  piece('detail', 'ring', AGENT_ANCHOR, [0, 0.34, -0.04], [0.58, 0.58, 0.58], PALETTE.peach);
}

function createTable(piece) {
  piece('metal', 'panel', 0, [0, 0.55, 0], [1.35, 1, 0.11], PALETTE.silver);
  piece('glass', 'panel', 0, [0, 0.55, 0.075], [1.27, 0.92, 0.014], PALETTE.glass);
  for (let row = 0; row < 6; row++) for (let column = 0; column < 6; column++) {
    const color = column === 0 ? [0xb8b4e9, 0xc5b8e8, 0xd2bfe8, 0xe4c6e4, 0xd0c2ec, PALETTE.paper][row]
      : column > 2 && row > 2 ? 0xf1d4c0 : PALETTE.paper;
    piece('paper', 'box', 0, [(column - 2.5) * 0.205, 0.92 - row * 0.146, 0.095],
      [0.195, 0.136, 0.008], color);
  }
}

function createGraph(THREE, piece, surfaces, shapes) {
  const nodes = [[-0.54, 0.49, 0.1], [-0.3, 0.97, 0], [0.17, 1.18, -0.08], [0.67, 0.86, 0],
    [0.53, 0.4, 0.25], [0.07, 0.24, 0.4], [-0.45, 0.2, 0.32], [0.13, 0.65, 0.15],
    [0.38, 0.92, 0.1], [-0.15, 0.67, -0.2]];
  nodes.forEach((position, index) => piece('detail', 'sphere', 1, position,
    Array(3).fill(index % 3 === 0 ? 0.096 : 0.11), index % 3 === 0 ? PALETTE.peach : PALETTE.lilac));
  for (const [a, b] of [[0, 1], [1, 2], [2, 3], [3, 4], [4, 5], [5, 6], [6, 0],
    [0, 7], [1, 7], [2, 8], [3, 8], [4, 7], [5, 7], [6, 9], [7, 8], [7, 9], [8, 9]]) {
    addRod(THREE, surfaces.metal, shapes.cylinder, 1, nodes[a], nodes[b], 0.008, PALETTE.edge);
  }
}

function createTimeSeries(THREE, piece, surfaces, shapes) {
  for (let index = 0; index < 27; index++) {
    const height = 0.19 + (Math.sin(index * 0.8) + 1) * 0.13 + Math.sin(index * 1.67) ** 2 * 0.34;
    const x = -0.82 + index * 0.062;
    piece('detail', 'cylinder', 2, [x, height / 2 + 0.1, Math.sin(index) * 0.09],
      [0.009, height, 0.009], index < 13 ? PALETTE.lilac : PALETTE.peach);
  }
  const points = Array.from({ length: 49 }, (_, index) => {
    const x = -0.82 + index * 1.64 / 48;
    return new THREE.Vector3(x, 0.4 + Math.sin(index / 48 * Math.PI * 2.5 - 0.7) * 0.17
      + index / 48 * 0.18, 0.32);
  });
  const tube = new THREE.TubeGeometry(new THREE.CatmullRomCurve3(points), 64, 0.025, 8, false);
  shapes.wave = tube;
  surfaces.detail.push({ geometry: tube, anchor: 2, offset: new THREE.Vector3(), size: new THREE.Vector3(1, 1, 1),
    rotation: new THREE.Quaternion(), color: PALETTE.lilac, gradient: true });
}

function createVectors(piece) {
  // Deterministic volumetric cloud; smooth beads use one shared surface draw.
  for (let index = 0; index < 210; index++) {
    const height = (index % 11) / 10, angle = index * 2.399963;
    const radius = Math.sqrt(((index * 37) % 211) / 211) * 0.64 * Math.sqrt(1 - (height - 0.5) ** 2);
    piece('detail', 'bead', 3, [Math.cos(angle) * radius, 0.22 + height * 0.82, Math.sin(angle) * radius * 0.72],
      Array(3).fill(0.015 + ((index * 7) % 5) * 0.0035), Math.cos(angle) > 0 ? PALETTE.peach : PALETTE.lilac);
  }
}

function createEvents(piece) {
  for (let index = 0; index < 3; index++) {
    piece('metal', 'orbit', 4, [0, 0.19 + index * 0.17, 0],
      [0.68 - index * 0.055, 0.68 - index * 0.055, 0.68 - index * 0.055], PALETTE.silver,
      [-Math.PI / 2 + index * 0.16, index * 0.1, index * 0.13]);
  }
  for (let index = 0; index < 10; index++) {
    const angle = index * 2.399963, radius = 0.59;
    piece('detail', 'sphere', 4, [Math.cos(angle) * radius, 0.19 + (index % 3) * 0.17,
      Math.sin(angle) * radius * 0.65], [0.055, 0.055, 0.055], index % 2 ? PALETTE.peach : PALETTE.lilac);
  }
}

function createDocuments(piece) {
  for (let index = 4; index >= 0; index--) {
    piece('paper', 'panel', 5, [index * 0.065 - 0.13, 0.56, -index * 0.12],
      [0.83, 0.97, 0.065], index % 2 ? 0xe3dad7 : PALETTE.paper);
  }
  for (let index = 0; index < 4; index++) piece('paper', 'box', 5, [-0.13, 0.83 - index * 0.14, 0.042],
    [index === 3 ? 0.35 : 0.54, 0.018, 0.012], PALETTE.ink);
}

function createQueue(piece) {
  piece('metal', 'panel', 6, [0, 0.12, 0], [1.45, 0.95, 0.13], PALETTE.edge, [-Math.PI / 2, 0, 0]);
  for (let index = 8; index >= 0; index--) {
    const color = [PALETTE.paper, 0xcfc8e4, 0xbbbae4, 0xe7c5ae][index % 4];
    piece('paper', 'panel', 6, [0, 0.48, 0.37 - index * 0.09], [1.04, 0.67, 0.05], color);
  }
  piece('detail', 'cylinder', 6, [-0.37, 0.62, 0.41], [0.047, 0.014, 0.047], PALETTE.peach,
    [Math.PI / 2, 0, 0]);
  for (let index = 0; index < 3; index++) piece('paper', 'box', 6, [0.06, 0.6 - index * 0.09, 0.405],
    [0.5, 0.02, 0.012], PALETTE.ink);
}

function createFiles(piece) {
  for (let index = 3; index >= 0; index--) {
    const z = 0.29 - index * 0.12, y = 0.46 + index * 0.035;
    const color = [PALETTE.paper, 0xd1c0e0, PALETTE.peach, 0xdcc9bb][index];
    piece('paper', 'panel', 7, [0, y, z], [1.05, 0.7, 0.055], color);
    piece('paper', 'panel', 7, [-0.26, y + 0.36, z - 0.035], [0.42, 0.12, 0.045], color);
    piece('paper', 'panel', 7, [0.06, y + 0.04, z - 0.058], [0.88, 0.72, 0.018], PALETTE.paper);
  }
}

function addRod(THREE, pieces, geometry, anchor, from, to, radius, color) {
  const start = new THREE.Vector3(...from), end = new THREE.Vector3(...to), direction = end.clone().sub(start);
  pieces.push({ geometry, anchor, offset: start.add(end).multiplyScalar(0.5),
    size: new THREE.Vector3(radius, direction.length(), radius), color,
    rotation: new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), direction.normalize()) });
}

function createSurface(THREE, own, root, anchors, pieces, material) {
  const count = pieces.reduce((sum, piece) => sum + (piece.geometry.index?.count
    ?? piece.geometry.getAttribute('position').count), 0);
  const positions = new Float32Array(count * 3), normals = new Float32Array(count * 3);
  const colors = new Float32Array(count * 3), modelAnchors = new Float32Array(count);
  const point = new THREE.Vector3(), normal = new THREE.Vector3(), color = new THREE.Color();
  let cursor = 0;
  for (const piece of pieces) {
    const geometry = piece.geometry, vertices = geometry.getAttribute('position'), sourceNormals = geometry.getAttribute('normal');
    const vertexCount = geometry.index?.count ?? vertices.count;
    color.setHex(piece.color);
    for (let index = 0; index < vertexCount; index++) {
      const vertex = geometry.index ? geometry.index.getX(index) : index;
      if (piece.gradient) color.setHex(PALETTE.lilac).lerp(new THREE.Color(PALETTE.peach),
        vertices.getX(vertex) / 1.64 + 0.5);
      point.fromBufferAttribute(vertices, vertex).multiply(piece.size).applyQuaternion(piece.rotation).add(piece.offset);
      normal.fromBufferAttribute(sourceNormals, vertex).divide(piece.size).applyQuaternion(piece.rotation).normalize();
      point.toArray(positions, cursor * 3);
      normal.toArray(normals, cursor * 3);
      color.toArray(colors, cursor * 3);
      modelAnchors[cursor++] = piece.anchor;
    }
  }
  const geometry = own(new THREE.BufferGeometry());
  geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
  geometry.setAttribute('normal', new THREE.BufferAttribute(normals, 3));
  geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3));
  geometry.setAttribute('modelAnchor', new THREE.BufferAttribute(modelAnchors, 1));
  material.positionNode = THREE.TSL.positionLocal.add(THREE.TSL.uniformArray(anchors, 'vec3')
    .element(THREE.TSL.attribute('modelAnchor', 'float').toInt()));
  const mesh = new THREE.Mesh(geometry, material);
  mesh.frustumCulled = false;
  root.add(mesh);
}

function createCables(THREE, own, root) {
  const curves = PLATFORMS.map((position, index) => {
    const angle = Math.atan2(position[0], position[2]);
    const start = new THREE.Vector3(Math.sin(angle) * 1.15, index < 2 ? 0.5 : index < 4 ? 0.06 : -0.38,
      Math.cos(angle) * 1.15);
    const end = new THREE.Vector3(position[0] * 0.82, position[1] + 0.08, position[2] * 0.82);
    return new THREE.CatmullRomCurve3([start, start.clone().lerp(end, 0.28).add(new THREE.Vector3(0, -0.08, 0)),
      start.clone().lerp(end, 0.72).add(new THREE.Vector3(0, -0.02, 0)), end]);
  });
  const anchors = [new THREE.Vector3()];
  for (const [radius, opacity] of [[0.05, 0.32], [0.019, 0.9]]) {
    const pieces = curves.map(curve => ({ geometry: new THREE.TubeGeometry(curve, 64, radius, 8, false),
      anchor: 0, offset: new THREE.Vector3(), size: new THREE.Vector3(1, 1, 1),
      rotation: new THREE.Quaternion(), color: PALETTE.white }));
    const material = own(new THREE.MeshPhysicalNodeMaterial({ transparent: true, opacity, depthWrite: false,
      metalness: 0.2, roughness: 0.15, clearcoat: 1, emissive: 0xbda3ea, emissiveIntensity: 0.17 }));
    material.colorNode = THREE.TSL.mix(THREE.TSL.color(PALETTE.lilac), THREE.TSL.color(PALETTE.peach),
      THREE.TSL.positionLocal.x.add(4).div(8).clamp());
    createSurface(THREE, own, root, anchors, pieces, material);
    pieces.forEach(piece => piece.geometry.dispose());
  }
  const packets = own(new THREE.InstancedMesh(own(new THREE.SphereGeometry(0.028, 12, 8)),
    own(new THREE.MeshBasicNodeMaterial({ color: PALETTE.white, transparent: true, opacity: 0.85 })), curves.length));
  const matrix = new THREE.Matrix4(), point = new THREE.Vector3();
  root.add(packets);
  return { count: curves.length, animate: seconds => {
    curves.forEach((curve, index) => {
      curve.getPoint((seconds * 0.17 + index / curves.length) % 1, point);
      packets.setMatrixAt(index, matrix.makeTranslation(point.x, point.y, point.z));
    });
    packets.instanceMatrix.needsUpdate = true;
  } };
}

function createStudioEnvironment(THREE, own) {
  const width = 128, height = 64, pixels = new Uint8Array(width * height * 4);
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const u = x / width, v = y / height;
    const softbox = Math.exp(-((u - 0.22) ** 2 * 400 + (v - 0.42) ** 2 * 9))
      + Math.exp(-((u - 0.72) ** 2 * 200 + (v - 0.4) ** 2 * 12));
    const peach = Math.exp(-((u - 0.48) ** 2 * 70 + (v - 0.61) ** 2 * 85));
    const iris = Math.exp(-((u - 0.88) ** 2 * 60 + (v - 0.58) ** 2 * 120));
    const offset = (y * width + x) * 4;
    pixels[offset] = Math.min(255, 174 + softbox * 81 + peach * 78 + iris * 20);
    pixels[offset + 1] = Math.min(255, 164 + softbox * 91 + peach * 48 + iris * 35);
    pixels[offset + 2] = Math.min(255, 167 + softbox * 88 + peach * 28 + iris * 85);
    pixels[offset + 3] = 255;
  }
  const texture = own(new THREE.DataTexture(pixels, width, height));
  texture.mapping = THREE.EquirectangularReflectionMapping;
  texture.colorSpace = THREE.SRGBColorSpace;
  texture.needsUpdate = true;
  return texture;
}

function createContactShade(THREE, own, root) {
  const width = 256, height = 192, pixels = new Uint8Array(width * height * 4);
  const anchors = [...PLATFORMS, [0, -0.88, 0]];
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const worldX = (x / width - 0.5) * 11.2, worldZ = (0.5 - y / height) * 7.8 + 0.7;
    const intensity = anchors.reduce((sum, anchor) => sum + Math.exp(-((worldX - anchor[0]) ** 2
      + (worldZ - anchor[2]) ** 2) * 3.1), 0);
    const offset = (y * width + x) * 4;
    pixels[offset] = 101; pixels[offset + 1] = 82; pixels[offset + 2] = 69;
    pixels[offset + 3] = Math.min(64, intensity * 62);
  }
  const texture = own(new THREE.DataTexture(pixels, width, height));
  texture.needsUpdate = true;
  const shade = new THREE.Mesh(own(new THREE.PlaneGeometry(11.2, 7.8)),
    own(new THREE.MeshBasicNodeMaterial({ map: texture, transparent: true, depthWrite: false })));
  shade.rotation.x = -Math.PI / 2;
  shade.position.set(0, -0.91, 0.7);
  root.add(shade);
}
