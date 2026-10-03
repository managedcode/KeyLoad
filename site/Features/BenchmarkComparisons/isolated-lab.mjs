import { loadIsolatedCatalog, loadIsolatedProjection } from './isolated-loader.mjs';
import { selectedIsolatedRows } from './isolated-measurements.mjs';
import { controlElements, initializeIsolatedControls, isolatedElements, readIsolatedSelection } from './isolated-controls.mjs';
import { renderIsolatedView } from './isolated-view.mjs';

function clear(elements) {
  elements.results.replaceChildren();
  elements.error.hidden = true;
  elements.retry.hidden = true;
  elements.announcement.textContent = '';
}

function render(state) {
  if (!state.projection || state.disposed) return;
  const selected = readIsolatedSelection(state.elements);
  const rows = selectedIsolatedRows(state.projection, selected.scenario, selected.nodeCount,
    selected.repetition, selected.metric, selected.target);
  renderIsolatedView(state.elements.results, state.catalog, state.projection, rows, selected);
  state.elements.announcement.textContent = `${rows.length} engine rows · ${selected.nodeCount} native nodes · ${selected.scenario}.`;
}

function fail(state) {
  state.catalog = null;
  state.projection = null;
  clear(state.elements);
  state.elements.error.textContent = 'Isolated evidence is unavailable or invalid. No measurements are displayed.';
  state.elements.error.hidden = false;
  state.elements.retry.hidden = false;
}

async function load(state) {
  const generation = ++state.generation;
  state.controller?.abort();
  state.controller = new AbortController();
  const signal = state.controller.signal;
  state.catalog = null;
  state.projection = null;
  clear(state.elements);
  state.elements.announcement.textContent = 'Loading verified isolated evidence…';
  try {
    const catalog = await loadIsolatedCatalog({ catalogUrl: state.catalogUrl, signal });
    const baseUrl = new URL('.', new URL(state.catalogUrl, state.elements.results.ownerDocument.baseURI)).href;
    const projection = await loadIsolatedProjection({ entry: catalog, baseUrl, signal });
    if (state.disposed || signal.aborted || generation !== state.generation) return;
    state.catalog = catalog;
    state.projection = projection;
    render(state);
  } catch {
    if (!state.disposed && !signal.aborted && generation === state.generation) fail(state);
  }
}

export function mountIsolatedLab({ root, catalogUrl }) {
  const elements = isolatedElements(root);
  initializeIsolatedControls(elements);
  const state = { elements, catalogUrl, disposed: false, generation: 0, controller: null, catalog: null, projection: null };
  const change = () => {
    try { render(state); }
    catch { fail(state); }
  };
  const retry = () => { void load(state); };
  const controls = controlElements(elements);
  for (const control of controls) control.addEventListener('change', change);
  elements.retry.addEventListener('click', retry);
  void load(state);
  return {
    dispose() {
      if (state.disposed) return;
      state.disposed = true;
      state.generation += 1;
      state.controller?.abort();
      state.catalog = null;
      state.projection = null;
      for (const control of controls) control.removeEventListener('change', change);
      elements.retry.removeEventListener('click', retry);
      clear(elements);
    },
  };
}
