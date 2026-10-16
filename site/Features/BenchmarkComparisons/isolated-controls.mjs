import { metrics } from './measurements.mjs';
import { DOM, assertIsolated } from './isolated-contracts.mjs';
import { SESSION, SESSION_TARGETS } from '../../../scripts/Features/BenchmarkComparisons/session-contract.mjs';

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
  options(elements.scenario, [...SESSION.caseIds, ...['create', 'delete', 'update', 'read', 'search'].map(lane => 'mixed/' + lane)]
    .map(value => [value, value]), 'sequential-read');
  options(elements.nodes, SESSION.nodes.map(value => [value, value + ' native nodes']), 3);
  options(elements.target, [['all', 'All engines'], ...SESSION_TARGETS.map(value => [value.name, value.name])], 'all');
  options(elements.metric, Object.entries(metrics).filter(([, metric]) => !metric.queue).map(([value, metric]) => [value, metric.title]), 'throughput');
  options(elements.repetition, [['session', 'Original session result']], 'session');
  elements.announcement.setAttribute('role', 'status');
  elements.announcement.setAttribute('aria-live', 'polite');
  elements.error.setAttribute('role', 'alert');
  elements.retry.type = 'button';
}

export function readIsolatedSelection(elements) {
  return { scenario: elements.scenario.value, nodeCount: Number(elements.nodes.value), target: elements.target.value,
    metric: elements.metric.value, repetition: elements.repetition.value };
}

export const controlElements = elements => [elements.scenario, elements.nodes, elements.target, elements.metric, elements.repetition];
