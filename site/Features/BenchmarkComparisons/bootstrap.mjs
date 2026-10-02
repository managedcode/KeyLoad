import { IDS, SELECTORS, CONFIG, TEXT } from './contracts.mjs';
import { mountBenchmarkLab } from './benchmark-lab.mjs';

const EVENTS = Object.freeze({ hide: 'pagehide', show: 'pageshow', click: 'click' });
const SCENE = Object.freeze({ module: './cluster-scene.mjs', unavailable: 'Static architectural illustration', delay: 100,
  hidden: 'hidden', initialGeneration: 0, nextGeneration: 1 });
let mounted = false;
let generation = SCENE.initialGeneration;
let lab;
let scene;
let deferred;
let copyReset;

function stop() {
  mounted = false;
  generation += SCENE.nextGeneration;
  clearTimeout(deferred);
  clearTimeout(copyReset);
  lab?.dispose();
  scene?.dispose();
  lab = undefined;
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
  lab = mountBenchmarkLab({ root: document, catalogUrl: CONFIG.catalogUrl });
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

window.addEventListener(EVENTS.hide, stop);
window.addEventListener(EVENTS.show, start);
bindCopy();
start();
