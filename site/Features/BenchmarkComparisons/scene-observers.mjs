import { SCENE, SCENE_TEXT } from './scene-geometry.mjs';

export function createSceneObservers({ host, motionButton, poster, statusElement, rendererFactory, sceneFactory }) {
  const state = createState(host, motionButton, poster, statusElement);
  const observers = {
    actions: null,
    rendererFactory,
    sceneFactory,
    install: callbacks => installObservers(state, observers, callbacks),
    stop: () => stopObservers(state),
    updateViewport: () => updateViewport(state),
  };
  return { state, observers };
}

function createState(host, motionButton, poster, statusElement) {
  const reducedMotion = globalThis.matchMedia?.(SCENE.media.reducedMotion) ?? null;
  const coarsePointer = globalThis.matchMedia?.(SCENE.media.coarsePointer) ?? null;
  return {
    host, motionButton, poster, statusElement, reducedMotion, coarsePointer,
    coreImage: host.querySelector(SCENE.core.selector),
    siloLabels: [...host.querySelectorAll('[data-silo-label]')],
    graphLabels: [...host.querySelectorAll('[data-graph-label]')],
    renderer: null, graph: null, resizeObserver: null, intersectionObserver: null, animationFrame: SCENE.math.zero,
    initializationPending: false, rendererDisposalQueued: false, initialized: false, terminal: false,
    disposed: false, failed: false, visible: false, pageActive: true, rendererReady: false,
    motionEnabled: false, pointerX: SCENE.math.zero, pointerY: SCENE.math.zero,
    lastPointerAt: SCENE.math.zero, lastFrameAt: SCENE.math.zero,
    width: SCENE.math.zero, height: SCENE.math.zero, canvasAttached: false, status: SCENE.state.poster,
    originalButtonDisabled: motionButton?.disabled ?? false,
    originalPressed: motionButton?.getAttribute(SCENE.attributes.ariaPressed) ?? null,
    originalButtonText: motionButton?.textContent ?? null,
    originalPosterHidden: poster?.hidden ?? false,
  };
}

function installObservers(state, observers, callbacks) {
  state.host.classList.add(SCENE.classes.host);
  state.host.dataset[SCENE.dataset.state] = SCENE.state.poster;
  state.host.dataset[SCENE.dataset.backend] = SCENE.backend.unknown;
  state.host.dataset[SCENE.dataset.frame] = SCENE.frame.idle;
  callbacks.showPoster(true);
  callbacks.writeStatus(SCENE.state.poster, SCENE_TEXT.poster);
  callbacks.configureMotionControl();
  state.resizeObserver = createResizeObserver(state, observers);
  state.intersectionObserver = createIntersectionObserver(state, observers, callbacks);
  addLifecycleListeners(state, observers, callbacks);
  updateViewport(state);
  if (!state.intersectionObserver) state.visible = intersectsViewport(state.host.getBoundingClientRect());
  observers.actions?.refresh();
}

function createResizeObserver(state, observers) {
  if (typeof ResizeObserver !== SCENE.types.function) return null;
  const observer = new ResizeObserver(() => observers.actions?.refresh());
  observer.observe(state.host);
  return observer;
}

function createIntersectionObserver(state, observers, callbacks) {
  if (typeof IntersectionObserver !== SCENE.types.function) return null;
  const observer = new IntersectionObserver(entries => {
    const entry = entries[entries.length - SCENE.math.one];
    state.visible = entry?.isIntersecting === true;
    if (!state.visible) callbacks.pauseScene();
    observers.actions?.refresh();
  }, { rootMargin: SCENE.observers.rootMargin });
  observer.observe(state.host);
  return observer;
}

function addLifecycleListeners(state, observers, callbacks) {
  const refresh = () => observers.actions?.refresh();
  const pointerMove = event => callbacks.handlePointerMove(event);
  const pageHide = () => { state.pageActive = false; callbacks.pauseScene(); };
  const pageShow = () => { state.pageActive = true; refresh(); };
  const visibility = () => {
    state.pageActive = document.visibilityState === SCENE.visibility.visible;
    if (!state.pageActive) callbacks.pauseScene();
    refresh();
  };
  const mediaChange = () => { callbacks.configureMotionControl(); refresh(); };
  state.host.addEventListener(SCENE.events.pointerMove, pointerMove, SCENE.observers.eventOptions);
  const toggleMotion = () => observers.actions?.setMotionEnabled(!state.motionEnabled);
  state.motionButton?.addEventListener(SCENE.events.click, toggleMotion);
  window.addEventListener(SCENE.events.resize, refresh, SCENE.observers.eventOptions);
  window.addEventListener(SCENE.events.pageHide, pageHide);
  window.addEventListener(SCENE.events.pageShow, pageShow);
  document.addEventListener(SCENE.events.visibility, visibility);
  state.reducedMotion?.addEventListener(SCENE.events.mediaChange, mediaChange);
  state.coarsePointer?.addEventListener(SCENE.events.mediaChange, mediaChange);
  state.listeners = { pointerMove, toggleMotion, refresh, pageHide, pageShow, visibility, mediaChange };
}

function intersectsViewport(rectangle) {
  return rectangle.width > SCENE.math.zero && rectangle.height > SCENE.math.zero && rectangle.bottom > SCENE.math.zero
    && rectangle.top < window.innerHeight && rectangle.right > SCENE.math.zero && rectangle.left < window.innerWidth;
}

function updateViewport(state) {
  if (!state.intersectionObserver) state.visible = intersectsViewport(state.host.getBoundingClientRect());
}

function stopObservers(state) {
  state.resizeObserver?.disconnect();
  state.intersectionObserver?.disconnect();
  const listeners = state.listeners;
  if (!listeners) return;
  state.host.removeEventListener(SCENE.events.pointerMove, listeners.pointerMove);
  state.motionButton?.removeEventListener(SCENE.events.click, listeners.toggleMotion);
  window.removeEventListener(SCENE.events.resize, listeners.refresh);
  window.removeEventListener(SCENE.events.pageHide, listeners.pageHide);
  window.removeEventListener(SCENE.events.pageShow, listeners.pageShow);
  document.removeEventListener(SCENE.events.visibility, listeners.visibility);
  state.reducedMotion?.removeEventListener(SCENE.events.mediaChange, listeners.mediaChange);
  state.coarsePointer?.removeEventListener(SCENE.events.mediaChange, listeners.mediaChange);
  state.listeners = null;
}
