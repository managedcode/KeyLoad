import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { AGGREGATE, exactKeys, KEYS, requireValue, validateCohort } from './aggregate-contracts.mjs';
import { readBytes, parseBytes, hashBytes } from './aggregate-files.mjs';
import { validateWorkerEnvelope } from './aggregate-validation.mjs';
import { requireWorkerImages } from './isolated-github-images.mjs';
import { matchesIsolatedJobName } from './isolated-github-contract.mjs';
import { validateWorkerMetadata, validateOptions } from '../../../site/Features/BenchmarkComparisons/isolated-metadata.mjs';
import { retainCommonFacts } from '../../../site/Features/BenchmarkComparisons/isolated-report-validation.mjs';
import { captureApi } from './isolated-github-api.mjs';
import { HISTORICAL, createHistoricalPlan, verifyHistoricalContractCapture } from './historical-isolated-plan.mjs';
export { verifyHistoricalContractCapture } from './historical-isolated-plan.mjs';

const check = condition => requireValue(condition, AGGREGATE.errors.proof);

export async function captureHistoricalContract(metadata, context, sourceRevision) {
  const route = `repos/managedcode/KeyLoad/contents/${HISTORICAL.path}?ref=${sourceRevision}`;
  const captured = await captureApi(route, path.join(metadata, HISTORICAL.capture), false, context);
  verifyHistoricalContractCapture(captured, sourceRevision);
  return captured;
}

export function validateHistoricalProof(value, plan, cohort) {
  check(exactKeys(value, KEYS.proof) && value.schemaVersion === AGGREGATE.proofVersion &&
    isDeepStrictEqual(value.cohort, cohort) && value.cells.length === plan.cells.length);
  validateCohort(cohort, plan.profile);
  const expected = new Map(plan.cells.map(cell => [cell.id, cell]));
  const jobs = new Set();
  const artifacts = new Set();
  for (const item of value.cells) {
    const cell = expected.get(item.id);
    check(cell !== undefined && exactKeys(item, KEYS.proofCell) && /^[a-f0-9]{64}$/u.test(item.workerSha256));
    const { family, ...metadata } = cell;
    validateWorkerMetadata({ ...metadata, disposition: item.job.conclusion === 'failure' ? 'failed' :
      cell.target === 'Neo4j' && cell.nodeCount > 1 ? 'unsupportedTopology' : 'measured', reason: item.job.conclusion === 'failure'
        ? AGGREGATE.failureReason : cell.target === 'Neo4j' && cell.nodeCount > 1
          ? 'Neo4j Community does not provide native clustering; Enterprise licensing is excluded.' : null,
      rawPath: `workers/${cell.id}/worker.json`, rawSha256: item.workerSha256, job: item.job, artifact: item.artifact },
    cell, cohort, { jobs, artifacts }, [...KEYS.cell.filter(key => key !== 'family'), 'disposition', 'reason', 'rawPath', 'rawSha256', 'job', 'artifact']);
    check(matchesIsolatedJobName(item.job.name, cell));
    expected.delete(item.id);
  }
  return value;
}

// Authentication precedes this function: original archives, selected jobs/artifacts and captured contract REST response remain required.
export async function produceHistoricalProjection({ input, sourceRevision, contractCapture, proof, workers, images }) {
  const contract = verifyHistoricalContractCapture(contractCapture, sourceRevision);
  const plan = createHistoricalPlan(sourceRevision);
  const manifest = parseBytes(await readBytes(path.join(input, 'aggregate.json'), AGGREGATE.metadataBytes));
  check(exactKeys(manifest, ['schemaVersion', 'cohort', 'profile', 'options', 'datasetSha256', 'workers']) &&
    manifest.schemaVersion === AGGREGATE.version && manifest.cohort.sourceRevision === sourceRevision &&
    manifest.profile === plan.profile && manifest.workers.length === plan.cells.length);
  validateOptions(manifest.options);
  validateHistoricalProof(proof, plan, manifest.cohort);
  const expected = new Map(plan.cells.map(cell => [cell.id, cell]));
  const byId = new Map(proof.cells.map(item => [item.id, item]));
  const common = { engines: new Map(), topologies: new Map() };
  const result = [];
  const identities = { jobs: new Set(), artifacts: new Set() };
  for (const item of manifest.workers) {
    const cell = expected.get(item.id);
    const native = byId.get(item.id);
    check(cell !== undefined && item.rawSha256 === native.workerSha256 && isDeepStrictEqual(item.job, native.job) &&
      isDeepStrictEqual(item.artifact, native.artifact));
    validateWorkerMetadata(item, cell, manifest.cohort, identities,
      ['id', 'target', 'nodeCount', 'scenario', 'profile', 'disposition', 'reason', 'rawPath', 'rawSha256', 'job', 'artifact']);
    if (workers) {
      const authenticated = workers.get(item.id);
      const { createdAt, ...artifact } = authenticated?.artifact ?? {};
      check(isDeepStrictEqual(item.job, authenticated?.job) && isDeepStrictEqual(item.artifact, artifact));
    }
    const bytes = await readBytes(path.join(input, item.rawPath), AGGREGATE.workerBytes);
    check(hashBytes(bytes) === item.rawSha256);
    const envelope = validateWorkerEnvelope(parseBytes(bytes), cell, manifest.cohort, contract);
    check(envelope.worker.jobId === item.job.id && envelope.disposition === item.disposition && envelope.reason === item.reason &&
      (envelope.report === null || envelope.report.datasetSha256 === manifest.datasetSha256));
    if (images) requireWorkerImages(envelope, images);
    retainCommonFacts(common, envelope.report, cell.nodeCount);
    result.push({ ...item, report: envelope.report === null ? null : { ...envelope.report,
      cases: envelope.report.cases.map(({ samples, ...entry }) => entry) } });
    expected.delete(item.id);
  }
  check(expected.size === 0 && manifest.datasetSha256 === (common.report?.datasetSha256 ?? null));
  return { ...manifest, schemaVersion: 1, workers: result };
}
