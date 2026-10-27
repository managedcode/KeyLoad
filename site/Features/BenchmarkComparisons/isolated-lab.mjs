import { loadIsolatedCatalog, loadIsolatedProjection } from './isolated-loader.mjs';
import { initializeIsolatedControls, isolatedElements, readIsolatedType, selectIsolatedType } from './isolated-controls.mjs';
import { isolatedModel, renderIsolatedSummary, renderIsolatedView } from './isolated-view.mjs';

function clear(elements) {
  elements.summary.replaceChildren();
  elements.results.replaceChildren();
  elements.error.hidden = true;
  elements.retry.hidden = true;
  elements.announcement.textContent = '';
}

function render(state) {
  if (!state.model || state.disposed) return;
  const type = readIsolatedType(state.elements);
  renderIsolatedView(state.elements.results, state.catalog, state.model, { type, sort: state.sort });
  const count = state.elements.results.querySelectorAll(type === 'all' ? '.ov-row' : '.db-card').length;
  state.elements.announcement.textContent = `${type === 'all' ? 'All databases' : type} · ${count} databases.`;
}

function fail(state) {
  state.catalog = null;
  state.model = null;
  clear(state.elements);
  state.elements.error.textContent = 'Benchmark results are unavailable or invalid. No measurements are displayed.';
  state.elements.error.hidden = false;
  state.elements.retry.hidden = false;
}

async function load(state) {
  const generation = ++state.generation;
  state.controller?.abort();
  state.controller = new AbortController();
  const signal = state.controller.signal;
  state.catalog = null;
  state.model = null;
  clear(state.elements);
  state.elements.announcement.textContent = 'Loading benchmark results…';
  try {
    const catalog = await loadIsolatedCatalog({ catalogUrl: state.catalogUrl, signal });
    const baseUrl = new URL('.', new URL(state.catalogUrl, state.elements.results.ownerDocument.baseURI)).href;
    const projection = await loadIsolatedProjection({ entry: catalog, baseUrl, signal });
    if (state.disposed || signal.aborted || generation !== state.generation) return;
    state.catalog = catalog;
    state.model = isolatedModel(projection);
    renderIsolatedSummary(state.elements.summary, state.model);
    render(state);
  } catch {
    if (!state.disposed && !signal.aborted && generation === state.generation) fail(state);
  }
}

export function mountIsolatedLab({ root, catalogUrl }) {
  const elements = isolatedElements(root);
  initializeIsolatedControls(elements);
  const state = { elements, catalogUrl, disposed: false, generation: 0, controller: null, catalog: null, model: null, sort: 'mixed' };
  const change = () => {
    try { render(state); }
    catch { fail(state); }
  };
  // Column headers sort the overview; engine names open their database family through the same radio group.
  const pick = event => {
    const target = event.target.closest?.('[data-sort], [data-type]');
    if (!target) return;
    if (target.dataset.sort) state.sort = target.dataset.sort;
    else if (!selectIsolatedType(elements, target.dataset.type)) return;
    change();
  };
  const retry = () => { void load(state); };
  elements.type.addEventListener('change', change);
  elements.results.addEventListener('click', pick);
  elements.retry.addEventListener('click', retry);
  void load(state);
  return {
    dispose() {
      if (state.disposed) return;
      state.disposed = true;
      state.generation += 1;
      state.controller?.abort();
      state.catalog = null;
      state.model = null;
      elements.type.removeEventListener('change', change);
      elements.results.removeEventListener('click', pick);
      elements.retry.removeEventListener('click', retry);
      clear(elements);
    },
  };
}
