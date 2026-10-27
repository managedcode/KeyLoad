import { loadIsolatedCatalog, loadIsolatedProjection } from './isolated-loader.mjs';
import { selectedIsolatedRows } from './isolated-measurements.mjs';
import { durationOnly } from './isolated-contracts.mjs';
import { controlElements, initializeIsolatedControls, isolatedElements, limitIsolatedTypes, readIsolatedSelection } from './isolated-controls.mjs';
import { renderIsolatedSummary, renderIsolatedView, renderNodeCounts } from './isolated-view.mjs';

function clear(elements) {
  elements.summary.replaceChildren();
  elements.results.replaceChildren();
  elements.error.hidden = true;
  elements.retry.hidden = true;
  elements.announcement.textContent = '';
}

function render(state) {
  if (!state.projection || state.disposed) return;
  const selected = readIsolatedSelection(state.elements);
  const rows = selectedIsolatedRows(state.projection, selected.scenario, selected.nodeCount,
    selected.repetition, selected.metric, 'all');
  state.elements.metric.hidden = durationOnly(selected.scenario);
  renderNodeCounts(state.elements.nodes, state.projection, selected);
  renderIsolatedView(state.elements.results, state.catalog, state.projection, rows, selected);
  const ranked = state.elements.results.querySelectorAll('.rank-row').length;
  state.elements.announcement.textContent = `${ranked} engines ranked · ${selected.nodeCount} execution nodes · ${selected.scenario}.`;
}

function fail(state) {
  state.catalog = null;
  state.projection = null;
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
  state.projection = null;
  clear(state.elements);
  state.elements.announcement.textContent = 'Loading benchmark results…';
  try {
    const catalog = await loadIsolatedCatalog({ catalogUrl: state.catalogUrl, signal });
    const baseUrl = new URL('.', new URL(state.catalogUrl, state.elements.results.ownerDocument.baseURI)).href;
    const projection = await loadIsolatedProjection({ entry: catalog, baseUrl, signal });
    if (state.disposed || signal.aborted || generation !== state.generation) return;
    state.catalog = catalog;
    state.projection = projection;
    limitIsolatedTypes(state.elements.type, renderIsolatedSummary(state.elements.summary, projection));
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
  // Matrix column headers select their scenario through the same radio group as the controls.
  const pick = event => {
    const scenario = event.target.closest?.('button[data-scenario]')?.dataset.scenario;
    const input = scenario && [...elements.scenario.querySelectorAll('input')].find(item => item.value === scenario);
    if (!input) return;
    input.checked = true;
    change();
  };
  const retry = () => { void load(state); };
  const controls = controlElements(elements);
  for (const control of controls) control.addEventListener('change', change);
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
      state.projection = null;
      for (const control of controls) control.removeEventListener('change', change);
      elements.results.removeEventListener('click', pick);
      elements.retry.removeEventListener('click', retry);
      clear(elements);
    },
  };
}
