import { metrics } from './measurements.mjs';
import { DOM, ISOLATED, assertIsolated } from './isolated-contracts.mjs';

export function isolatedElements(root) {
  const elements = Object.fromEntries(Object.entries(DOM).map(([key, id]) => [key, root.querySelector('#' + id)]));
  assertIsolated(Object.values(elements).every(Boolean));
  return elements;
}

function options(select, entries, initial) {
  const fragment = select.ownerDocument.createDocumentFragment();
  for (const [value, label] of entries) {
    const option = select.ownerDocument.createElement('option');
    option.value = String(value);
    option.textContent = label;
    fragment.append(option);
  }
  select.replaceChildren(fragment);
  select.value = String(initial);
}

export function initializeIsolatedControls(elements) {
  options(elements.scenario, [...ISOLATED.crud, ...ISOLATED.specialized].map(value => [value, value]), 'PointRead');
  options(elements.nodes, ISOLATED.nodes.map(value => [value, value + ' native nodes']), 3);
  options(elements.target, [['all', 'All engines'], ...ISOLATED.targets.map(value => [value, value])], 'all');
  options(elements.metric, Object.entries(metrics).map(([value, metric]) => [value, metric.title]), 'throughput');
  options(elements.repetition, [['all', 'Median of five repetitions'], ...Array.from({ length: ISOLATED.options.repetitions },
    (_, value) => [value, 'Repetition ' + value])], 'all');
  elements.announcement.setAttribute('role', 'status');
  elements.announcement.setAttribute('aria-live', 'polite');
  elements.error.setAttribute('role', 'alert');
  elements.retry.type = 'button';
}

export function readIsolatedSelection(elements) {
  for (const option of elements.metric.options) {
    option.disabled = Boolean(metrics[option.value]?.queue) && elements.scenario.value !== 'QueueCycle';
  }
  if (elements.metric.selectedOptions[0].disabled) elements.metric.value = 'throughput';
  return { scenario: elements.scenario.value, nodeCount: Number(elements.nodes.value), target: elements.target.value,
    metric: elements.metric.value, repetition: elements.repetition.value === 'all' ? 'all' : Number(elements.repetition.value) };
}

export const controlElements = elements => [elements.scenario, elements.nodes, elements.target, elements.metric, elements.repetition];
