import * as THREE from './vendor/three/0.186.1/three.webgpu.js';
import { SELECTORS } from './contracts.mjs';
import { SCENE, SCENE_TEXT, createSceneGraph } from './scene-geometry.mjs';
import { createSceneLifecycle } from './scene-lifecycle.mjs';

export async function mountClusterScene({ host, motionButton }) {
  if (!host) throw new TypeError(SCENE_TEXT.missingHost);
  const poster = host.querySelector(SELECTORS.poster);
  const statusElement = host.querySelector(SELECTORS.sceneStatus);
  return createSceneLifecycle({
    host,
    motionButton,
    poster,
    statusElement,
    rendererFactory: () => new THREE.WebGPURenderer({ ...SCENE.renderer }),
    sceneFactory: () => createSceneGraph(THREE),
  });
}
