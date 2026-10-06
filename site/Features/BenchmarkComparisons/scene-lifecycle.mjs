import { SCENE, SCENE_TEXT } from './scene-geometry.mjs';
import { createSceneObservers } from './scene-observers.mjs';

let rendererGate = Promise.resolve();

export function createSceneLifecycle({ host, motionButton, poster, statusElement, rendererFactory, sceneFactory }) {
  const { state, observers } = createSceneObservers({
    host, motionButton, poster, statusElement, rendererFactory, sceneFactory,
  });
  const actions = createActions(state, observers);
  state.observers = observers;
  observers.actions = actions;
  state.setMotionEnabled = actions.setMotionEnabled;
  state.dispose = actions.dispose;
  observers.install({
    configureMotionControl: () => configureMotionControl(state),
    handlePointerMove: event => handlePointerMove(state, event),
    pauseScene: () => pauseScene(state),
    showPoster: visible => showPoster(state, visible),
    writeStatus: (status, message) => writeStatus(state, status, message),
  });
  return { dispose: actions.dispose, setMotionEnabled: actions.setMotionEnabled };
}

function createActions(state, observers) {
  function refresh() {
    observers.updateViewport();
    refreshSize(state, observers);
    maybeInitialize(state, observers);
  }
  return {
    setMotionEnabled: enabled => setMotionEnabled(state, enabled),
    dispose: () => disposeScene(state, observers),
    refresh,
    observers,
  };
}

function refreshSize(state, observers) {
  if (state.terminal) return;
  const rectangle = state.host.getBoundingClientRect();
  state.width = rectangle.width;
  state.height = rectangle.height;
  if (state.width <= SCENE.math.zero || state.height <= SCENE.math.zero) {
    state.width = SCENE.math.zero;
    state.height = SCENE.math.zero;
    cancelFrame(state);
    showPoster(state, true);
    writeStatus(state, SCENE.state.zeroSize, SCENE_TEXT.zeroSize);
    return;
  }
  if (state.renderer && state.canvasAttached) resizeRenderer(state);
  if (state.initialized && canRender(state)) scheduleFrame(state);
}

function maybeInitialize(state, observers) {
  if (state.terminal || state.initialized || !canInitialize(state)) return;
  state.initialized = true;
  writeStatus(state, SCENE.state.loading, SCENE_TEXT.loading);
  queueInitialization(state, observers);
}

function queueInitialization(state, observers) {
  const task = rendererGate.then(() => state.terminal ? undefined : initializeRenderer(state, observers));
  rendererGate = task.catch(() => {});
}

function canInitialize(state) {
  return state.visible && state.pageActive && !document.hidden && state.width > SCENE.math.zero
    && state.height > SCENE.math.zero;
}

async function initializeRenderer(state, observers) {
  let renderer;
  try {
    renderer = observers.rendererFactory();
    state.renderer = renderer;
    state.initializationPending = true;
    installRendererHooks(state);
    state.graph = observers.sceneFactory();
    await state.coreImage.decode();
    if (state.terminal) return releaseResources(state);
    await renderer.init();
    state.initializationPending = false;
    if (state.terminal) return releaseResources(state);
    if (!detectBackend(state)) return failScene(state, SCENE.state.unsupported, SCENE_TEXT.unsupported);
    attachCanvas(state);
    resizeRenderer(state);
    if (canRender(state)) scheduleFrame(state);
    else pauseScene(state);
  } catch {
    state.initializationPending = false;
    if (state.terminal) releaseResources(state);
    else failScene(state, SCENE.state.error, SCENE_TEXT.error);
  }
}

function installRendererHooks(state) {
  state.renderer.onDeviceLost = () => failScene(state, SCENE.state.deviceLost, SCENE_TEXT.deviceLost);
  state.renderer.onError = () => failScene(state, SCENE.state.error, SCENE_TEXT.error);
}

function detectBackend(state) {
  const backend = state.renderer.backend;
  if (backend?.isWebGPUBackend === true) state.host.dataset[SCENE.dataset.backend] = SCENE.backend.webgpu;
  else if (backend?.isWebGLBackend === true) state.host.dataset[SCENE.dataset.backend] = SCENE.backend.webgl;
  else {
    state.host.dataset[SCENE.dataset.backend] = SCENE.backend.unknown;
    return false;
  }
  return true;
}

function attachCanvas(state) {
  const canvas = state.renderer.domElement;
  canvas.classList.add(SCENE.classes.canvas);
  canvas.setAttribute(SCENE.attributes.ariaHidden, String(true));
  state.host.append(canvas);
  state.canvasAttached = true;
}

function resizeRenderer(state) {
  const deviceRatio = Math.min(window.devicePixelRatio || SCENE.math.one, SCENE.limits.maxDevicePixelRatio);
  const areaRatio = Math.sqrt(SCENE.limits.maxBufferPixels / (state.width * state.height));
  const ratio = Math.min(deviceRatio, areaRatio);
  state.renderer.setPixelRatio(ratio);
  state.renderer.setSize(state.width, state.height, false);
  state.graph.resize(state.width / state.height);
  state.graph.camera.updateProjectionMatrix();
  const pixels = state.renderer.domElement.width * state.renderer.domElement.height;
  if (pixels > SCENE.limits.maxBufferPixels) {
    state.renderer.setPixelRatio(ratio * Math.sqrt(SCENE.limits.maxBufferPixels / pixels));
    state.renderer.setSize(state.width, state.height, false);
  }
}

function canRender(state) {
  return !state.terminal && state.renderer && state.graph && state.visible && state.pageActive && !document.hidden
    && state.width > SCENE.math.zero && state.height > SCENE.math.zero;
}

function scheduleFrame(state, continuing = false) {
  if (state.animationFrame || !canRender(state)) return;
  if (!continuing) state.host.dataset[SCENE.dataset.frame] = SCENE.frame.requested;
  state.animationFrame = requestAnimationFrame(time => renderFrame(state, time));
}

function renderFrame(state, time) {
  state.animationFrame = SCENE.math.zero;
  if (!canRender(state)) return pauseScene(state);
  try {
    updateSceneMotion(state, time);
    state.renderer.render(state.graph.scene, state.graph.camera);
    if (state.terminal) return;
    projectCoreImage(state);
    projectSiloLabels(state);
    recordFrame(state);
    state.rendererReady = true;
    configureMotionControl(state);
    showPoster(state, false);
    writeStatus(state, SCENE.state.ready, SCENE_TEXT.ready);
    startMotionOnce(state);
    if (state.motionEnabled || isSettling(state, time)) scheduleFrame(state, true);
  } catch {
    failScene(state, SCENE.state.error, SCENE_TEXT.error);
  }
}

function projectCoreImage(state) {
  if (!state.coreImage) return;
  const projection = state.graph.projectCore(state.width, state.height);
  state.coreImage.style.width = projection.size + SCENE.core.pixels;
  state.coreImage.style.height = projection.size + SCENE.core.pixels;
  state.coreImage.style.transform = projection.transform;
}

function projectSiloLabels(state) {
  const positions = state.graph.projectLabels(state.width, state.height);
  state.siloLabels.forEach((label, index) => {
    label.style.left = positions[index].x + SCENE.core.pixels;
    label.style.top = positions[index].y + SCENE.core.pixels;
  });
}

function updateSceneMotion(state, time) {
  const root = state.graph.root;
  const targetYaw = state.motionEnabled ? state.pointerX * SCENE.world.pointerYaw : SCENE.math.zero;
  const targetPitch = state.motionEnabled ? state.pointerY * SCENE.world.pointerPitch : SCENE.math.zero;
  const elapsed = state.lastFrameAt === SCENE.math.zero ? SCENE.limits.settleMilliseconds : time - state.lastFrameAt;
  const fraction = Math.min(SCENE.math.one, elapsed / SCENE.world.smoothingDivisor);
  root.rotation.y += (targetYaw - root.rotation.y) * fraction;
  root.rotation.x += (targetPitch - root.rotation.x) * fraction;
  if (state.motionEnabled) {
    state.spinMilliseconds = (state.spinMilliseconds ?? SCENE.math.zero) + Math.min(elapsed, SCENE.limits.maxSpinStepMilliseconds);
    state.graph.animate?.(state.spinMilliseconds / SCENE.world.millisecondsPerSecond);
  }
  if (state.lastPointerAt > SCENE.math.zero && time - state.lastPointerAt >= SCENE.limits.settleRenderMilliseconds) {
    root.rotation.y = targetYaw;
    root.rotation.x = targetPitch;
  }
  state.lastFrameAt = time;
}

function isSettling(state, time) {
  if (!state.motionEnabled || state.lastPointerAt === SCENE.math.zero) return false;
  const yaw = state.pointerX * SCENE.world.pointerYaw;
  const pitch = state.pointerY * SCENE.world.pointerPitch;
  const delta = Math.abs(state.graph.root.rotation.y - yaw) + Math.abs(state.graph.root.rotation.x - pitch);
  return time - state.lastPointerAt < SCENE.limits.settleRenderMilliseconds && delta > SCENE.math.epsilon;
}

function handlePointerMove(state, event) {
  if (state.terminal || !state.motionEnabled || !isFinePointer(state)) return;
  const rectangle = state.host.getBoundingClientRect();
  if (rectangle.width <= SCENE.math.zero || rectangle.height <= SCENE.math.zero) return;
  state.pointerX = Math.max(SCENE.math.negativeOne, Math.min(SCENE.math.one,
    ((event.clientX - rectangle.left) / rectangle.width) * SCENE.math.two - SCENE.math.one));
  state.pointerY = Math.max(SCENE.math.negativeOne, Math.min(SCENE.math.one,
    ((event.clientY - rectangle.top) / rectangle.height) * SCENE.math.two - SCENE.math.one));
  state.lastPointerAt = performance.now();
  scheduleFrame(state);
}

function recordFrame(state) {
  state.host.dataset.sceneSilos = String(state.graph.counts.silos);
  state.host.dataset.sceneGrains = String(state.graph.counts.grains);
  state.host.dataset.sceneLinks = String(state.graph.counts.links);
  const metrics = state.renderer.info.render;
  const canvas = state.renderer.domElement;
  const bufferPixels = canvas.width * canvas.height;
  state.host.dataset[SCENE.dataset.pixels] = String(bufferPixels);
  state.host.dataset[SCENE.dataset.calls] = String(metrics.drawCalls);
  state.host.dataset[SCENE.dataset.renderCalls] = String(metrics.calls);
  state.host.dataset[SCENE.dataset.triangles] = String(metrics.triangles);
  state.host.dataset[SCENE.dataset.frame] = SCENE.frame.rendered;
  if (bufferPixels > SCENE.limits.maxBufferPixels || metrics.drawCalls > SCENE.limits.maxDrawCalls
      || metrics.triangles > SCENE.limits.maxTriangles) {
    throw new RangeError(SCENE_TEXT.error);
  }
}

function pauseScene(state) {
  cancelFrame(state);
  if (!state.terminal && state.initialized) {
    state.host.dataset[SCENE.dataset.frame] = SCENE.frame.paused;
    showPoster(state, true);
    writeStatus(state, SCENE.state.paused, SCENE_TEXT.paused);
  }
}

/** The scene moves as soon as it is ready unless the visitor prefers reduced motion. */
function startMotionOnce(state) {
  if (state.motionStarted) return;
  state.motionStarted = true;
  setMotionEnabled(state, true);
}

function setMotionEnabled(state, enabled) {
  if (state.terminal) return;
  const next = Boolean(enabled) && state.rendererReady && motionAllowed(state);
  if (next === state.motionEnabled) return;
  state.motionEnabled = next;
  state.motionButton?.setAttribute(SCENE.attributes.ariaPressed, String(state.motionEnabled));
  configureMotionControl(state);
  if (!state.motionEnabled) {
    state.pointerX = SCENE.math.zero;
    state.pointerY = SCENE.math.zero;
    if (state.graph) {
      state.graph.root.rotation.x = SCENE.math.zero;
      state.graph.root.rotation.y = SCENE.math.zero;
    }
    state.lastPointerAt = SCENE.math.zero;
  }
  if (canRender(state)) scheduleFrame(state);
}

function configureMotionControl(state) {
  const restricted = !motionAllowed(state);
  const unavailable = restricted || state.terminal;
  if (state.motionButton) state.motionButton.disabled = !state.rendererReady || unavailable;
  if (state.motionButton) {
    state.motionButton.textContent = unavailable ? SCENE_TEXT.motionUnavailable
      : state.motionEnabled ? SCENE_TEXT.disableMotion : SCENE_TEXT.enableMotion;
    state.motionButton.setAttribute(SCENE.attributes.ariaPressed, String(state.motionEnabled));
  }
  if (restricted && state.motionEnabled) setMotionEnabled(state, false);
}

function motionAllowed(state) {
  return state.reducedMotion?.matches !== true;
}

function isFinePointer(state) {
  return state.reducedMotion?.matches !== true && state.coarsePointer?.matches !== true;
}

function writeStatus(state, status, message) {
  state.status = status;
  state.host.dataset[SCENE.dataset.state] = status;
  if (state.statusElement) state.statusElement.textContent = message;
}

function showPoster(state, visible) {
  if (state.poster) state.poster.hidden = visible ? state.originalPosterHidden : true;
  if (state.coreImage) state.coreImage.hidden = visible;
}

function cancelFrame(state) {
  if (state.animationFrame) cancelAnimationFrame(state.animationFrame);
  state.animationFrame = SCENE.math.zero;
}

function failScene(state, status, message) {
  if (state.terminal) return;
  state.failed = true;
  state.terminal = true;
  state.motionEnabled = false;
  cancelFrame(state);
  writeStatus(state, status, message);
  state.host.dataset[SCENE.dataset.frame] = SCENE.frame.error;
  configureMotionControl(state);
  showPoster(state, true);
  state.observers?.stop?.();
  releaseResources(state);
}

function disposeScene(state, observers) {
  if (state.disposed) return;
  state.disposed = true;
  state.terminal = true;
  cancelFrame(state);
  observers.stop();
  releaseResources(state);
  showPoster(state, true);
  writeStatus(state, SCENE.state.disposed, SCENE_TEXT.disposed);
  state.host.dataset[SCENE.dataset.frame] = SCENE.frame.idle;
  state.host.classList.remove(SCENE.classes.host);
  restoreMotionControl(state);
}

function releaseResources(state) {
  if (state.graph) state.graph.dispose();
  state.graph = null;
  state.coreImage?.removeAttribute(SCENE.attributes.style);
  if (state.canvasAttached) state.renderer?.domElement?.remove();
  state.canvasAttached = false;
  queueRendererDisposal(state);
}

function queueRendererDisposal(state) {
  if (!state.renderer || state.rendererDisposalQueued) return;
  state.rendererDisposalQueued = true;
  const renderer = state.renderer;
  const release = rendererGate.then(() => renderer.dispose());
  rendererGate = release.catch(() => {});
  void rendererGate.then(() => { if (state.renderer === renderer) state.renderer = null; });
}

function restoreMotionControl(state) {
  if (!state.motionButton) return;
  state.motionButton.disabled = state.originalButtonDisabled;
  if (state.originalPressed === null) state.motionButton.removeAttribute(SCENE.attributes.ariaPressed);
  else state.motionButton.setAttribute(SCENE.attributes.ariaPressed, state.originalPressed);
  if (state.originalButtonText !== null) state.motionButton.textContent = state.originalButtonText;
}
