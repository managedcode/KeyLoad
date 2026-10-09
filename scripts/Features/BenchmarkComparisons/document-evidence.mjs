import { isDeepStrictEqual } from 'node:util';
import { AGGREGATE, KEYS, SUPPORT, exactKeys, requireValue, validateCohort } from './aggregate-contracts.mjs';
import { DOCUMENT, createDocumentPlan, readDocumentContract } from './document-isolated-plan.mjs';
import { readIsolatedContract } from './isolated-plan-contract.mjs';

const failure = 'E_DOCUMENT_EVIDENCE';
const check = value => requireValue(value, failure);
const integer = value => Number.isSafeInteger(value) && value >= 0;
const finite = value => typeof value === 'number' && Number.isFinite(value) && value >= 0;
const selection = cell => ({ scenario: cell.documentScenario, records: cell.documentRecords, clients: cell.documentClients });
const reportKeys = ['schemaVersion', 'family', 'selection', 'status', 'qualified', 'repetitions'];
const repetitionKeys = ['repetition', 'target', 'status', 'qualified', 'errors', 'initialRecords', 'addedRecords', 'deletedRecords',
  'expectedFinalRecords', 'actualFinalRecords', 'expectedSha256', 'actualSha256', 'requestedClients', 'openedClients', 'peakInFlight',
  'planned', 'attempts', 'acknowledged', 'failed', 'canceled', 'unfinished', 'singleOperationTiming', 'phases', 'histogram', 'clientResources'];
const phaseKeys = ['setupSeconds', 'warmupSeconds', 'measuredSeconds', 'verificationSeconds', 'cleanupSeconds',
  'loadSeconds', 'indexBuildSeconds', 'indexBuildApplicable', 'phaseInstrumentationComplete'];
const histogramKeys = ['resolutionMicroseconds', 'maximumMilliseconds', 'count', 'overflowCount', 'bucketIndices', 'bucketCounts',
  'p50Milliseconds', 'p95Milliseconds', 'p99Milliseconds', 'maximumObservedMilliseconds'];
export function documentUnsupportedReason(cell) {
  const topology = readIsolatedContract().unsupportedTopologies.find(item => item.target === cell.target && item.nodeCounts.includes(cell.nodeCount));
  if (topology !== undefined) return { disposition: 'unsupportedTopology', reason: topology.reason };
  const support = SUPPORT[cell.target] ?? [];
  const required = { SequentialRead: ['PointRead'], RandomRead: ['PointRead'], Create: ['DocumentWrite'],
    Update: ['DocumentUpdate'], Delete: ['DocumentDelete'], ReadUpdate50: ['PointRead', 'DocumentUpdate'],
    ReadUpdate95: ['PointRead', 'DocumentUpdate'], MixedCrud: ['PointRead', 'DocumentWrite', 'DocumentUpdate', 'DocumentDelete'], Ingest: ['DocumentWrite'] };
  if (!required[cell.documentScenario]?.every(scenario => support.includes(scenario))) {
    return { disposition: 'unsupported', reason: `${cell.target} does not implement ${cell.documentScenario} natively.` };
  }
  return null;
}
export function validateDocumentEnvelope(value, cell, cohort) {
  check(isDeepStrictEqual(createDocumentPlan().cells.find(item => item.id === cell?.id), cell));
  validateCohort(cohort, cell.profile);
  check(exactKeys(value, ['schemaVersion', 'kind', 'worker', 'document', 'disposition', 'reason', 'report'])
    && value.schemaVersion === DOCUMENT.schemaVersion && value.kind === DOCUMENT.kind && exactKeys(value.worker, KEYS.worker)
    && isDeepStrictEqual(value.document, selection(cell)) && value.worker.target === cell.target && value.worker.nodeCount === cell.nodeCount
    && value.worker.scenario === cell.scenario && value.worker.profile === cell.profile && Number.isSafeInteger(value.worker.jobId) && value.worker.jobId > 0
    && Object.keys(cohort).every(key => value.worker[key] === cohort[key]));
  const unsupported = documentUnsupportedReason(cell);
  if (value.disposition === 'failed') {
    check(value.reason === AGGREGATE.failureReason && value.report === null);
  } else if (unsupported !== null) {
    check(value.disposition === unsupported.disposition && value.reason === unsupported.reason && value.report === null);
  } else {
    check(value.disposition === 'measured' && value.reason === null);
    validateDocumentReport(value.report, cell);
  }
  return value;
}
export function validateDocumentReport(report, cell) {
  const contract = readDocumentContract();
  check(exactKeys(report, reportKeys) && report.schemaVersion === 1 && report.family === DOCUMENT.family
    && isDeepStrictEqual(report.selection, { scenario: cell.documentScenario, datasetRecords: cell.documentRecords, clients: cell.documentClients })
    && report.status === 'measured' && report.qualified === true && Array.isArray(report.repetitions) && report.repetitions.length === contract.repetitions);
  for (const [index, repetition] of report.repetitions.entries()) validateRepetition(repetition, index, cell, contract);
  return report;
}
function expectedCounts(cell, contract) {
  const initial = cell.documentScenario === 'Ingest' ? 0 : cell.documentRecords;
  const planned = cell.documentScenario === 'Ingest' ? contract.ingestion.records : contract.operations;
  const added = cell.documentScenario === 'Ingest' || cell.documentScenario === 'Create' ? planned : cell.documentScenario === 'MixedCrud' ? planned / 4 : 0;
  const deleted = cell.documentScenario === 'Delete' ? planned : cell.documentScenario === 'MixedCrud' ? planned / 4 : 0;
  return { initial, planned, added, deleted, final: initial + added - deleted };
}
function validateRepetition(value, index, cell, contract) {
  const counts = expectedCounts(cell, contract);
  check(exactKeys(value, repetitionKeys) && value.repetition === index && value.status === 'measured' && value.qualified === true
    && Array.isArray(value.errors) && value.errors.length === 0 && exactKeys(value.target, KEYS.target) && value.target.name === cell.target
    && AGGREGATE.image.test(value.target.image ?? '') && exactKeys(value.target.cluster, KEYS.cluster)
    && ['version', 'topology', 'writeAcknowledgement', 'readContract', 'transport', 'authorization'].every(key => typeof value.target[key] === 'string' && value.target[key].length > 0)
    && typeof value.target.cluster.state === 'string' && value.target.cluster.state.length > 0
    && Array.isArray(value.target.cluster.observations) && value.target.cluster.observations.length > 0
    && value.target.cluster.observations.every(item => typeof item === 'string' && item.length > 0)
    && value.target.cluster.nodes === cell.nodeCount && value.target.cluster.dataCopies === cell.nodeCount
    && value.initialRecords === counts.initial && value.addedRecords === counts.added && value.deletedRecords === counts.deleted
    && value.expectedFinalRecords === counts.final && value.actualFinalRecords === counts.final
    && AGGREGATE.digest.test(value.expectedSha256 ?? '') && value.expectedSha256 === value.actualSha256
    && value.requestedClients === cell.documentClients && value.openedClients === cell.documentClients
    && integer(value.peakInFlight) && value.peakInFlight > 0 && value.peakInFlight <= cell.documentClients
    && value.planned === counts.planned && value.attempts === counts.planned && value.acknowledged === counts.planned
    && value.failed === 0 && value.canceled === 0 && value.unfinished === 0 && value.singleOperationTiming === true
    && exactKeys(value.phases, phaseKeys)
    && ['setupSeconds', 'warmupSeconds', 'measuredSeconds', 'verificationSeconds', 'cleanupSeconds', 'loadSeconds', 'indexBuildSeconds'].every(key => finite(value.phases[key]))
    && typeof value.phases.indexBuildApplicable === 'boolean' && value.phases.phaseInstrumentationComplete === true
    && (value.phases.indexBuildApplicable || value.phases.indexBuildSeconds === 0) && value.phases.measuredSeconds > 0);
  const resources = value.clientResources;
  check(exactKeys(resources, ['cpuSeconds', 'allocatedBytes', 'peakObservedWorkingSetBytes', 'samplingIntervalMs'])
    && finite(resources.cpuSeconds) && integer(resources.allocatedBytes) && integer(resources.peakObservedWorkingSetBytes)
    && integer(resources.samplingIntervalMs) && resources.samplingIntervalMs > 0);
  validateHistogram(value.histogram, value.attempts, contract.latencyHistogram);
}
function validateHistogram(value, attempts, policy) {
  check(exactKeys(value, histogramKeys) && value.resolutionMicroseconds === policy.resolutionMicroseconds
    && value.maximumMilliseconds === policy.maximumMilliseconds && value.count === attempts && value.overflowCount === 0
    && Array.isArray(value.bucketIndices) && Array.isArray(value.bucketCounts) && value.bucketIndices.length === value.bucketCounts.length
    && value.bucketIndices.length > 0 && value.bucketIndices.length <= attempts);
  let count = 0;
  let previous = -1;
  for (const [index, bucket] of value.bucketIndices.entries()) {
    check(integer(bucket) && bucket > previous && bucket * policy.resolutionMicroseconds <= policy.maximumMilliseconds * 1000
      && integer(value.bucketCounts[index]) && value.bucketCounts[index] > 0);
    count += value.bucketCounts[index]; previous = bucket;
  }
  check(count === attempts && [value.p50Milliseconds, value.p95Milliseconds, value.p99Milliseconds, value.maximumObservedMilliseconds].every(finite)
    && value.p50Milliseconds <= value.p95Milliseconds && value.p95Milliseconds <= value.p99Milliseconds
    && value.maximumObservedMilliseconds <= policy.maximumMilliseconds
    && Math.ceil(value.maximumObservedMilliseconds * 1000 / policy.resolutionMicroseconds) === previous);
  for (const [field, fraction] of [['p50Milliseconds', .5], ['p95Milliseconds', .95], ['p99Milliseconds', .99]]) {
    const threshold = Math.ceil(attempts * fraction);
    let cumulative = 0; let percentile = null;
    for (const [index, bucket] of value.bucketIndices.entries()) {
      cumulative += value.bucketCounts[index];
      if (cumulative >= threshold) { percentile = bucket * policy.resolutionMicroseconds / 1000; break; }
    }
    check(value[field] === percentile);
  }
}

export function validateDocumentComparableReports(cells) {
  const digests = new Map();
  for (const item of cells) {
    if (item.disposition !== 'measured') continue;
    validateDocumentReport(item.report, item.cell);
    for (const repetition of item.report.repetitions) {
      const key = JSON.stringify([item.cell.profile, repetition.repetition]);
      const previous = digests.get(key);
      check(previous === undefined || previous === repetition.expectedSha256);
      digests.set(key, repetition.expectedSha256);
    }
  }
}

// Pool original counts and histogram bins; percentile averages do not describe the combined distribution.
export function documentStatistics(report, cell) {
  validateDocumentReport(report, cell);
  const repetitions = report.repetitions;
  const throughputs = repetitions.map(value => value.acknowledged / value.phases.measuredSeconds);
  const mean = throughputs.reduce((total, value) => total + value, 0) / throughputs.length;
  const deviation = Math.sqrt(throughputs.reduce((total, value) => total + (value - mean) ** 2, 0) / (throughputs.length - 1));
  const operations = repetitions.reduce((total, value) => total + value.acknowledged, 0);
  const seconds = repetitions.reduce((total, value) => total + value.phases.measuredSeconds, 0);
  const bins = new Map();
  for (const value of repetitions) {
    value.histogram.bucketIndices.forEach((bucket, index) => bins.set(bucket, (bins.get(bucket) ?? 0) + value.histogram.bucketCounts[index]));
  }
  const ordered = [...bins].sort(([left], [right]) => left - right);
  const quantile = fraction => {
    const threshold = Math.ceil(operations * fraction);
    let cumulative = 0;
    for (const [bucket, count] of ordered) {
      cumulative += count;
      if (cumulative >= threshold) return bucket * repetitions[0].histogram.resolutionMicroseconds / 1000;
    }
    throw new Error(failure);
  };
  return { repetitions: repetitions.length, acknowledged: operations, measuredSeconds: seconds,
    pooledThroughputOperationsPerSecond: operations / seconds,
    meanThroughputOperationsPerSecond: mean, throughputSampleStandardDeviation: deviation,
    throughputCoefficientOfVariation: deviation / mean,
    minimumThroughputOperationsPerSecond: Math.min(...throughputs), maximumThroughputOperationsPerSecond: Math.max(...throughputs),
    pooledLatencyMilliseconds: { p50: quantile(.5), p95: quantile(.95), p99: quantile(.99),
      maximumObserved: Math.max(...repetitions.map(value => value.histogram.maximumObservedMilliseconds)) } };
}
