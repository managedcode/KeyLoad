import { metrics } from './measurements.mjs';
import { DOM, ENGINE_TYPES, SCENARIOS, TYPE_ORDER, assertIsolated, durationOnly } from './isolated-contracts.mjs';
import { SESSION } from '../../../scripts/Features/BenchmarkComparisons/session-contract.mjs';

const METRIC_LABELS = Object.freeze({ throughput: 'Throughput', p50: 'p50 latency', p95: 'p95 latency',
  p99: 'p99 latency', duration: 'Duration', errors: 'Errors' });

export function isolatedElements(root) {
  const elements = Object.fromEntries(Object.entries(DOM).map(([key, id]) => [key, root.querySelector('#' + id)]));
  assertIsolated(Object.values(elements).every(Boolean));
  return elements;
}

function chips(fieldset, entries, initial) {
  const document = fieldset.ownerDocument;
  fieldset.querySelector('.chip-row')?.remove();
  const row = fieldset.appendChild(document.createElement('span'));
  row.className = 'chip-row';
  // A labelled group wraps as one unit so its chips never split across lines.
  let parent = row;
  for (const [value, label, group] of entries) {
    if (group) {
      parent = row.appendChild(document.createElement('span'));
      parent.className = 'chip-set';
      parent.appendChild(document.createElement('span')).textContent = group;
    }
    const chip = document.createElement('label');
    const input = document.createElement('input');
    const text = document.createElement('span');
    chip.className = 'chip';
    input.type = 'radio';
    input.name = fieldset.id;
    input.value = String(value);
    input.checked = String(value) === String(initial);
    text.textContent = label;
    chip.append(input, text);
    parent.append(chip);
  }
}

export function initializeIsolatedControls(elements) {
  chips(elements.nodes, SESSION.nodes.map(value => [value, value + (value === 1 ? ' node' : ' nodes')]), 1);
  chips(elements.scenario, SCENARIOS.map((item, index) => [item.value, item.label,
    item.mixed && !SCENARIOS[index - 1].mixed ? 'Mixed workload' : null]), 'sequential-read');
  chips(elements.metric, Object.keys(METRIC_LABELS).filter(key => Object.hasOwn(metrics, key))
    .map(key => [key, METRIC_LABELS[key]]), 'throughput');
  chips(elements.type, [['all', 'All types'], ...TYPE_ORDER.map(type => [type, type])], 'all');
  elements.announcement.setAttribute('role', 'status');
  elements.announcement.setAttribute('aria-live', 'polite');
  elements.error.setAttribute('role', 'alert');
  elements.retry.type = 'button';
}

// Families without any measured engine stay out of the filter; the board still names those engines and why.
export function limitIsolatedTypes(fieldset, engines) {
  for (const input of fieldset.querySelectorAll('input')) {
    input.parentElement.hidden = input.value !== 'all' && ![...engines].some(name => ENGINE_TYPES[name] === input.value);
  }
}

const checked = fieldset => fieldset.querySelector('input:checked').value;

export function readIsolatedSelection(elements) {
  const scenario = checked(elements.scenario);
  return { scenario, nodeCount: Number(checked(elements.nodes)), type: checked(elements.type),
    metric: durationOnly(scenario) ? 'duration' : checked(elements.metric), repetition: 'session' };
}

export const controlElements = elements => [elements.scenario, elements.nodes, elements.metric, elements.type];
