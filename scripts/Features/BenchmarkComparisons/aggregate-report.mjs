import { AGGREGATE, KEYS, SUPPORT, exactKeys, matches, positive, requireValue, text } from './aggregate-contracts.mjs';
import { validateMeasurement } from './sample-metrics.mjs';

const ERROR = AGGREGATE.errors.report;
const PLATFORM_FIELDS = Object.freeze(['loadModel', 'hostOs', 'architecture', 'runtime', 'storage']);
const TARGET_TEXT_FIELDS = Object.freeze(['version', 'topology', 'writeAcknowledgement', 'readContract', 'transport', 'authorization']);
const DATE = /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?(?:Z|[+-]\d\d:\d\d)$/;

function validateOptions(options, cell, contract) {
  requireValue(exactKeys(options, [...Object.keys(contract.options), 'topology']) &&
    options.topology === AGGREGATE.topology[cell.nodeCount] &&
    Object.entries(contract.options).every(([key, value]) => options[key] === value), ERROR);
}

function validateTarget(target, cell) {
  requireValue(exactKeys(target, KEYS.target) && target.name === cell.target &&
    TARGET_TEXT_FIELDS.every(field => text(target[field])) && matches(AGGREGATE.image, target.image), ERROR);
  const cluster = target.cluster;
  requireValue(exactKeys(cluster, KEYS.cluster) && cluster.nodes === cell.nodeCount && cluster.dataCopies === cell.nodeCount &&
    text(cluster.state) && Array.isArray(cluster.observations) && cluster.observations.length > 0 &&
    cluster.observations.every(text), ERROR);
}

function validateCase(item, cell, options, repetitions) {
  requireValue(exactKeys(item, KEYS.case) && item.target === cell.target && item.scenario === cell.scenario &&
    Number.isSafeInteger(item.repetition) && item.repetition >= 0 && item.repetition < options.repetitions &&
    !repetitions.has(item.repetition), ERROR);
  repetitions.add(item.repetition);
  if (!SUPPORT[cell.target].includes(cell.scenario)) {
    requireValue(item.status === AGGREGATE.unsupported && text(item.detail) && item.measurement === null &&
      Array.isArray(item.samples) && item.samples.length === 0, ERROR);
    return;
  }
  requireValue(item.status === AGGREGATE.measured && item.detail === null, ERROR);
  validateMeasuredCase(item, options, cell.scenario);
}

function validateMeasuredCase(item, options, scenario) {
  requireValue(exactKeys(item.measurement, KEYS.measurement) && exactKeys(item.measurement.latency, KEYS.latency) &&
    Array.isArray(item.samples) && item.samples.length === options.operations, ERROR);
  requireValue(item.samples.every(sample => exactKeys(sample, KEYS.sample) &&
    (sample.queue === null || exactKeys(sample.queue, KEYS.queue))), ERROR);
  for (const stage of ['enqueue', 'receive', 'ack']) {
    requireValue(item.measurement[stage] === null || exactKeys(item.measurement[stage], KEYS.latency), ERROR);
  }
  const result = validateMeasurement(item.measurement, item.samples, options, scenario);
  requireValue(result !== null && result.successes === options.operations && result.failures === 0, ERROR);
}

export function validateReport(report, cell, cohort, contract) {
  requireValue(exactKeys(report, KEYS.report) && report.schemaVersion === AGGREGATE.reportVersion &&
    matches(AGGREGATE.guid, report.runId) && matches(DATE, report.startedAt) && Number.isFinite(Date.parse(report.startedAt)) &&
    report.sourceRevision === cohort.sourceRevision && matches(AGGREGATE.digest, report.datasetSha256) &&
    PLATFORM_FIELDS.every(field => text(report[field])) && matches(AGGREGATE.linux, report.hostOs) &&
    positive(report.logicalProcessors) && matches(AGGREGATE.image, report.loadGeneratorImage), ERROR);
  requireValue(exactKeys(report.provenance, KEYS.provenance) &&
    KEYS.provenance.every(key => report.provenance[key] === cohort[key]), ERROR);
  validateOptions(report.options, cell, contract);
  requireValue(Array.isArray(report.targets) && report.targets.length === 1, ERROR);
  validateTarget(report.targets[0], cell);
  requireValue(Array.isArray(report.cases) && report.cases.length === contract.options.repetitions, ERROR);
  const repetitions = new Set();
  for (const item of report.cases) validateCase(item, cell, report.options, repetitions);
  return report;
}
