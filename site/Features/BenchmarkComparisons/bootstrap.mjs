import { IDS, SELECTORS, CONFIG, TEXT } from './contracts.mjs';
import { mountIsolatedLab } from './isolated-lab.mjs';

const EVENTS = Object.freeze({ hide: 'pagehide', show: 'pageshow', click: 'click', change: 'change' });
const SCENE = Object.freeze({ module: './cluster-scene.mjs', unavailable: 'Static architectural illustration', delay: 100,
  hidden: 'hidden', initialGeneration: 0, nextGeneration: 1 });
let mounted = false;
let generation = SCENE.initialGeneration;
let isolatedLab;
let scene;
let deferred;
let copyReset;

function stop() {
  mounted = false;
  generation += SCENE.nextGeneration;
  clearTimeout(deferred);
  clearTimeout(copyReset);
  isolatedLab?.dispose();
  scene?.dispose();
  isolatedLab = undefined;
  scene = undefined;
}

async function startScene(token) {
  const host = document.getElementById(IDS.scene);
  const motionButton = document.getElementById(IDS.motion);
  if (!host || token !== generation) return;
  try {
    const { mountClusterScene } = await import(SCENE.module);
    if (token !== generation) return;
    const instance = await mountClusterScene({ host, motionButton });
    if (token !== generation) instance.dispose();
    else scene = instance;
  } catch {
    if (token !== generation) return;
    host.querySelector(SELECTORS.poster)?.removeAttribute(SCENE.hidden);
    const status = host.querySelector(SELECTORS.sceneStatus);
    if (status) status.textContent = SCENE.unavailable;
    if (motionButton) motionButton.disabled = true;
  }
}

function start() {
  if (mounted) return;
  mounted = true;
  const token = ++generation;
  const isolatedRoot = document.getElementById(IDS.benchmarks);
  if (isolatedRoot?.dataset.isolatedCatalog) {
    isolatedLab = mountIsolatedLab({ root: isolatedRoot, catalogUrl: isolatedRoot.dataset.isolatedCatalog });
  }
  deferred = setTimeout(() => startScene(token), SCENE.delay);
}

function bindCopy() {
  const button = document.querySelector(SELECTORS.copy);
  const code = document.querySelector(SELECTORS.command);
  if (!button || !code || !navigator.clipboard) return;
  button.addEventListener(EVENTS.click, async () => {
    const token = generation;
    try {
      await navigator.clipboard.writeText(code.textContent);
      if (token !== generation) return;
      button.textContent = TEXT.copied;
      clearTimeout(copyReset);
      copyReset = setTimeout(() => { button.textContent = TEXT.copy; }, CONFIG.copyResetMs);
    } catch {
      if (token !== generation) return;
      button.textContent = TEXT.sourceCheckout;
    }
  });
}

function bindMenu() {
  const menu = document.getElementById(IDS.menu);
  if (!menu?.hidePopover) return;
  const close = () => { if (menu.matches(SELECTORS.menuOpen)) menu.hidePopover(); };
  menu.addEventListener(EVENTS.click, event => {
    if (!event.target.closest(SELECTORS.menuLink)) return;
    // Focus left inside the popover is restored to the toggle on hide, which cancels the fragment scroll.
    document.activeElement?.blur();
    close();
  });
  matchMedia(CONFIG.wideNavigationQuery).addEventListener(EVENTS.change, close);
}

window.addEventListener(EVENTS.hide, stop);
window.addEventListener(EVENTS.show, start);
bindCopy();
bindMenu();
start();
