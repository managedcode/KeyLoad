import { metrics, selectedRows } from './measurements.mjs';
import { ISOLATED, assertIsolated } from './isolated-contracts.mjs';

function unavailable(worker) {
  const count = worker.disposition === ISOLATED.failed ? null : 0;
  return { name: worker.target, status: worker.disposition, value: null, min: null, max: null, attempts: count,
    successes: count, failures: count, throughput: null, p50: null, p95: null, p99: null, detail: worker.reason,
    nodeCount: worker.nodeCount, worker };
}

export function selectedIsolatedRows(projection, scenario, nodeCount, repetition, metric, target) {
  assertIsolated([...ISOLATED.crud, ...ISOLATED.specialized].includes(scenario) && ISOLATED.nodes.includes(nodeCount) &&
    Object.hasOwn(metrics, metric) && (target === 'all' || ISOLATED.targets.includes(target)) &&
    (repetition === 'all' || Number.isSafeInteger(repetition) && repetition >= 0 && repetition < ISOLATED.options.repetitions));
  const workers = projection.workers.filter(worker => worker.scenario === scenario && worker.nodeCount === nodeCount &&
    (target === 'all' || worker.target === target));
  const rows = workers.map(worker => {
    if (worker.disposition === 'unsupportedTopology' || worker.disposition === ISOLATED.failed) return unavailable(worker);
    const row = selectedRows(worker.report, scenario, repetition === 'all' ? 'median' : repetition, metric)[0];
    return { ...row, nodeCount, worker };
  });
  // Each original report contains exactly one engine and one node count; its median is never a pooled cohort.
  return rows.sort((left, right) => {
    if (left.value === null) return right.value === null ? 0 : 1;
    if (right.value === null) return -1;
    return metrics[metric].higher ? right.value - left.value : left.value - right.value;
  });
}
