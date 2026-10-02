export const SCENE = Object.freeze({
  limits: Object.freeze({ maxDevicePixelRatio: 1.5, maxBufferPixels: 1_000_000, maxDrawCalls: 30, maxTriangles: 5_000,
    settleMilliseconds: 500, settleRenderMilliseconds: 450 }),
  world: Object.freeze({ hostCount: 3, hostSpacing: 3.1, hostY: 1.1, hostWidth: 1.55, hostHeight: 2.15,
    hostDepth: 1.05, tileWidth: 0.48, tileHeight: 0.34, tileDepth: 0.08, tileY: 0.62, tileCenterOffset: 0.31,
    groundY: -0.08, cameraFov: 30, cameraNear: 0.1, cameraFar: 60, cameraX: 7.2, cameraY: 6.1,
    cameraZ: 11.6, cameraLookY: 1.45, cameraAspect: 1, pointerYaw: 0.055, pointerPitch: 0.025,
    smoothingDivisor: 115, floorDepth: 4.8, floorWidth: 10.5, frontInset: 0.05, plinthHeight: 0.12,
    plinthWidthExtra: 0.25, plinthDepthExtra: 0.2, plinthY: 0.04, railY: 1.7, railWidthFraction: 0.72,
    railHeight: 0.07, hostCenterIndex: 1, firstTileIndex: 0, halfTurn: Math.PI / 2,
    tileDepthCenter: 2, cameraTargetX: 0, cameraTargetZ: 0, tokenY: 3.55, tokenSize: 0.42, tokenTilt: 0.32,
    beamRadius: 0.028, beamSegments: 8, beamTargetY: 2.2, discRadius: 5.1, discSegments: 48, discY: -0.06,
    fogNear: 10, fogFar: 24 }),
  material: Object.freeze({ hostRoughness: 0.38, hostMetalness: 0.22, baseRoughness: 0.9,
    firstTileRoughness: 0.4, secondTileRoughness: 0.55, railRoughness: 0.5,
    hemisphereSky: 0xffffff, hemisphereGround: 0x2a2d33, hemisphereIntensity: 1.7,
    directionalColor: 0xffffff, directionalIntensity: 2.8, glowIntensity: 0.6, tokenGlow: 0.45, pointIntensity: 9, pointDistance: 8 }),
  math: Object.freeze({ zero: 0, half: 0.5, one: 1, two: 2, negativeOne: -1, epsilon: 0.001 }),
  colors: Object.freeze({ host: 0x3b3f48, base: 0x4a4f59, partitionA: 0xc6f24e,
    partitionB: 0xc9b4ff, rail: 0xffb08a, ground: 0x0c0d10, sky: 0x0c0d10, disc: 0x15171b,
    token: 0xc6f24e, beam: 0xa4d82f }),
  state: Object.freeze({ poster: 'poster', loading: 'loading', ready: 'ready', paused: 'paused', zeroSize: 'zero-size',
    error: 'error', deviceLost: 'device-lost', unsupported: 'unsupported', disposed: 'disposed' }),
  frame: Object.freeze({ idle: 'idle', requested: 'requested', rendered: 'rendered', paused: 'paused', error: 'error' }),
  backend: Object.freeze({ webgpu: 'webgpu', webgl: 'webgl2', unknown: 'unknown' }),
  renderer: Object.freeze({ powerPreference: 'low-power', alpha: true, antialias: false }),
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
  poster: 'The static cluster illustration is visible.',
  loading: 'Loading a conceptual cluster illustration.',
  ready: 'Conceptual cluster illustration active; this is not live topology.',
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
  const geometry = (value) => (geometries.add(value), value);
  const material = (value) => (materials.add(value), value);
  const box = geometry(new THREE.BoxGeometry(SCENE.math.one, SCENE.math.one, SCENE.math.one));
  const ground = geometry(new THREE.PlaneGeometry(SCENE.world.floorWidth, SCENE.world.floorDepth));
  const hostMaterial = material(new THREE.MeshStandardNodeMaterial({ color: SCENE.colors.host,
    roughness: SCENE.material.hostRoughness, metalness: SCENE.material.hostMetalness }));
  const baseMaterial = material(new THREE.MeshStandardNodeMaterial({ color: SCENE.colors.base,
    roughness: SCENE.material.baseRoughness }));
  const partitionMaterials = [
    material(new THREE.MeshStandardNodeMaterial({ color: SCENE.colors.partitionA, emissive: SCENE.colors.partitionA,
      emissiveIntensity: SCENE.material.glowIntensity, roughness: SCENE.material.firstTileRoughness })),
    material(new THREE.MeshStandardNodeMaterial({ color: SCENE.colors.partitionB,
      roughness: SCENE.material.secondTileRoughness })),
  ];
  const railMaterial = material(new THREE.MeshStandardNodeMaterial({ color: SCENE.colors.rail,
    roughness: SCENE.material.railRoughness }));
  const groundMaterial = material(new THREE.MeshBasicNodeMaterial({ color: SCENE.colors.ground }));
  scene.background = new THREE.Color(SCENE.colors.sky);
  scene.fog = new THREE.Fog(SCENE.colors.sky, SCENE.world.fogNear, SCENE.world.fogFar);
  scene.add(new THREE.HemisphereLight(SCENE.material.hemisphereSky, SCENE.material.hemisphereGround,
    SCENE.material.hemisphereIntensity));
  scene.add(new THREE.DirectionalLight(SCENE.material.directionalColor, SCENE.material.directionalIntensity));
  scene.add(root);
  const floor = new THREE.Mesh(ground, groundMaterial);
  floor.rotation.x = -SCENE.world.halfTurn;
  floor.position.y = SCENE.world.groundY;
  root.add(floor);
  for (let index = SCENE.math.zero; index < SCENE.world.hostCount; index += SCENE.math.one) {
    addHost(THREE, root, index, box, hostMaterial, baseMaterial, partitionMaterials, railMaterial);
  }
  addPlatform(THREE, root, geometry, material);
  addReplication(THREE, root, geometry, material);
  camera.position.set(SCENE.world.cameraX, SCENE.world.cameraY, SCENE.world.cameraZ);
  camera.lookAt(SCENE.world.cameraTargetX, SCENE.world.cameraLookY, SCENE.world.cameraTargetZ);
  return { scene, camera, root, dispose: () => disposeGraph(geometries, materials) };
}

function addHost(THREE, root, index, box, hostMaterial, baseMaterial, partitionMaterials, railMaterial) {
  const x = (index - SCENE.world.hostCenterIndex) * SCENE.world.hostSpacing;
  const group = new THREE.Group();
  group.position.x = x;
  const chassis = new THREE.Mesh(box, hostMaterial);
  chassis.scale.set(SCENE.world.hostWidth, SCENE.world.hostHeight, SCENE.world.hostDepth);
  chassis.position.y = SCENE.world.hostY;
  group.add(chassis);
  const plinth = new THREE.Mesh(box, baseMaterial);
  plinth.scale.set(SCENE.world.hostWidth + SCENE.world.plinthWidthExtra, SCENE.world.plinthHeight,
    SCENE.world.hostDepth + SCENE.world.plinthDepthExtra);
  plinth.position.y = SCENE.world.plinthY;
  group.add(plinth);
  addPartitionTiles(THREE, group, box, partitionMaterials);
  addRail(THREE, group, box, railMaterial);
  root.add(group);
}

function addPartitionTiles(THREE, group, box, materials) {
  for (let index = SCENE.math.zero; index < materials.length; index += SCENE.math.one) {
    const tile = new THREE.Mesh(box, materials[index]);
    tile.scale.set(SCENE.world.tileWidth, SCENE.world.tileHeight, SCENE.world.tileDepth);
    tile.position.set(index === SCENE.world.firstTileIndex ? -SCENE.world.tileCenterOffset : SCENE.world.tileCenterOffset,
      SCENE.world.tileY, SCENE.world.hostDepth / SCENE.world.tileDepthCenter + SCENE.world.frontInset);
    group.add(tile);
  }
}

function addRail(THREE, group, box, railMaterial) {
  const rail = new THREE.Mesh(box, railMaterial);
  rail.scale.set(SCENE.world.hostWidth * SCENE.world.railWidthFraction, SCENE.world.railHeight, SCENE.world.tileDepth);
  rail.position.set(SCENE.math.zero, SCENE.world.railY,
    SCENE.world.hostDepth / SCENE.world.tileDepthCenter + SCENE.world.frontInset);
  group.add(rail);
}

function addPlatform(THREE, root, geometry, material) {
  const disc = new THREE.Mesh(geometry(new THREE.CircleGeometry(SCENE.world.discRadius, SCENE.world.discSegments)),
    material(new THREE.MeshBasicNodeMaterial({ color: SCENE.colors.disc })));
  disc.rotation.x = -SCENE.world.halfTurn;
  disc.position.y = SCENE.world.discY;
  root.add(disc);
}

function addReplication(THREE, root, geometry, material) {
  const token = new THREE.Mesh(geometry(new THREE.OctahedronGeometry(SCENE.world.tokenSize)),
    material(new THREE.MeshStandardNodeMaterial({ color: SCENE.colors.token, emissive: SCENE.colors.token,
      emissiveIntensity: SCENE.material.tokenGlow, roughness: SCENE.material.firstTileRoughness })));
  token.position.y = SCENE.world.tokenY;
  const glow = new THREE.PointLight(SCENE.colors.token, SCENE.material.pointIntensity, SCENE.material.pointDistance);
  glow.position.copy(token.position);
  root.add(glow);
  token.rotation.set(SCENE.world.tokenTilt, SCENE.world.tokenTilt * SCENE.math.two, SCENE.world.tokenTilt);
  root.add(token);
  const beamGeometry = geometry(new THREE.CylinderGeometry(SCENE.world.beamRadius, SCENE.world.beamRadius,
    SCENE.math.one, SCENE.world.beamSegments, SCENE.math.one, true));
  const beamMaterial = material(new THREE.MeshStandardNodeMaterial({ color: SCENE.colors.beam,
    emissive: SCENE.colors.beam, emissiveIntensity: SCENE.material.tokenGlow }));
  const up = new THREE.Vector3(SCENE.math.zero, SCENE.math.one, SCENE.math.zero);
  for (let index = SCENE.math.zero; index < SCENE.world.hostCount; index += SCENE.math.one) {
    const target = new THREE.Vector3((index - SCENE.world.hostCenterIndex) * SCENE.world.hostSpacing,
      SCENE.world.beamTargetY, SCENE.math.zero);
    const direction = target.clone().sub(token.position);
    const beam = new THREE.Mesh(beamGeometry, beamMaterial);
    beam.scale.y = direction.length();
    beam.position.copy(token.position).addScaledVector(direction, SCENE.math.half);
    beam.quaternion.setFromUnitVectors(up, direction.normalize());
    root.add(beam);
  }
}

function disposeGraph(geometries, materials) {
  for (const value of geometries) value.dispose();
  for (const value of materials) value.dispose();
  geometries.clear();
  materials.clear();
}
