import { ISOLATED, SUPPORT, WIRE, assertIsolated, date, exact, matches, positive, same, text } from './isolated-contracts.mjs';
import { validateOptions } from './isolated-metadata.mjs';
import { validateCompactMeasurement } from './isolated-metrics-validation.mjs';

const PLATFORM = Object.freeze(['loadModel', 'hostOs', 'architecture', 'runtime', 'storage']);
const TARGET_TEXT = Object.freeze(['version', 'topology', 'writeAcknowledgement', 'readContract', 'transport', 'authorization']);
const COMMON = Object.freeze(['datasetSha256', 'architecture', 'loadGeneratorImage', 'loadModel']);
const ENGINE = Object.freeze(['image', 'version', 'transport', 'authorization']);
const TOPOLOGY = Object.freeze(['topology', 'writeAcknowledgement', 'readContract']);

function validateTarget(target, worker) {
  assertIsolated(exact(target, WIRE.target) && target.name === worker.target && TARGET_TEXT.every(key => text(target[key])) &&
    matches(ISOLATED.image, target.image));
  const cluster = target.cluster;
  assertIsolated(exact(cluster, WIRE.cluster) && cluster.nodes === worker.nodeCount && cluster.dataCopies === worker.nodeCount &&
    text(cluster.state) && Array.isArray(cluster.observations) && cluster.observations.length > 0 && cluster.observations.every(text));
}

function validateCase(item, worker, repetitions) {
  assertIsolated(exact(item, WIRE.case) && item.target === worker.target && item.scenario === worker.scenario &&
    Number.isSafeInteger(item.repetition) && item.repetition >= 0 && item.repetition < ISOLATED.options.repetitions &&
    !repetitions.has(item.repetition));
  repetitions.add(item.repetition);
  if (!SUPPORT[worker.target].includes(worker.scenario)) {
    assertIsolated(item.status === 'unsupported' && text(item.detail) && item.measurement === null);
    return;
  }
  assertIsolated(item.status === 'measured' && item.detail === null);
  validateCompactMeasurement(item.measurement, worker.scenario);
}

export function validateCompactReport(report, worker, cohort, datasetSha256) {
  if (worker.disposition === 'unsupportedTopology') {
    assertIsolated(report === null);
    return;
  }
  assertIsolated(exact(report, WIRE.report) && report.schemaVersion === ISOLATED.reportVersion &&
    matches(ISOLATED.guid, report.runId) && date(report.startedAt) && report.sourceRevision === cohort.sourceRevision &&
    report.datasetSha256 === datasetSha256 && PLATFORM.every(key => text(report[key])) &&
    matches(ISOLATED.linux, report.hostOs) && positive(report.logicalProcessors) && matches(ISOLATED.image, report.loadGeneratorImage));
  assertIsolated(exact(report.provenance, WIRE.provenance) && same(report.provenance, cohort, WIRE.provenance));
  validateOptions(report.options, worker.nodeCount);
  assertIsolated(Array.isArray(report.targets) && report.targets.length === 1 &&
    Array.isArray(report.cases) && report.cases.length === ISOLATED.options.repetitions);
  validateTarget(report.targets[0], worker);
  const repetitions = new Set();
  for (const item of report.cases) validateCase(item, worker, repetitions);
}

export function retainCommonFacts(common, report, nodeCount) {
  if (report === null) return;
  assertIsolated(common.report === undefined || same(report, common.report, COMMON));
  common.report ??= Object.fromEntries(COMMON.map(key => [key, report[key]]));
  const target = report.targets[0];
  const prior = common.engines.get(target.name);
  assertIsolated(prior === undefined || same(target, prior, ENGINE));
  common.engines.set(target.name, target);
  const key = target.name + '\0' + nodeCount;
  const topology = common.topologies.get(key);
  assertIsolated(topology === undefined || same(target, topology, TOPOLOGY));
  common.topologies.set(key, target);
}
