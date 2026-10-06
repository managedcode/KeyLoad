import { isDeepStrictEqual } from 'node:util';
import { isolatedJobName } from '../../../site/Features/BenchmarkComparisons/isolated-contracts.mjs';
import { validateOpenLoopPlan } from './open-loop-isolated-plan.mjs';
import { CELL_TERMINAL, exactKeys, lowercaseHash, positive, reject, sourceSha } from './open-loop-cell-terminal-contract.mjs';
import { validateCellTerminalForPlan } from './open-loop-cell-terminal-validation.mjs';
import { readIsolatedContract } from './isolated-plan.mjs';

const RECEIPT_KIND = 'open-loop-cohort-receipt.v1';
const FAILURE = 'failure';
const SUCCESS = 'success';
const JOB_URL = 'https://github.com/managedcode/KeyLoad/actions/runs/';
const STEPS = Object.freeze(['Run database workload', 'Save benchmark results']);

export function validateOpenLoopCohortInput(input) {
  reject(exactKeys(input, ['plan', 'cohort', 'cells']) && Array.isArray(input.cells)
    && input.cells.length === CELL_TERMINAL.maximumCells);
  const plan = validateOpenLoopPlan(input.plan);
  const cohort = validateCohort(input.cohort);
  const expected = [...plan.measurementCells, ...plan.cancellationProofCells];
  const jobs = new Set();
  const artifacts = new Set();
  const cells = expected.map((cell, index) => validateInputCell(input.cells[index], cell, plan, cohort, jobs, artifacts));
  reject(new Set(cells.map(item => item.cell.id)).size === expected.length);
  return { plan, cohort, cells, counts: countCells(cells) };
}

function validateInputCell(item, cell, plan, cohort, jobs, artifacts) {
  reject(exactKeys(item, ['cell', 'terminal', 'terminalSha256', 'job', 'artifact'])
    && isDeepStrictEqual(item.cell, cell) && lowercaseHash.test(item.terminalSha256));
  const terminal = validateCellTerminalForPlan(item.terminal, plan, cell, cohort);
  validateJob(item.job, terminal, cell, cohort, jobs);
  validateArtifact(item.artifact, cell, artifacts);
  return { cell, terminal, terminalSha256: item.terminalSha256, job: item.job, artifact: item.artifact };
}

export function createOpenLoopCohortReceipt(input, planSha256) {
  reject(lowercaseHash.test(planSha256));
  const valid = validateOpenLoopCohortInput(input);
  const failedCellIds = valid.cells.filter(item => item.terminal.disposition === 'failed')
    .map(item => item.cell.id).sort(ordinal);
  const receipt = { schemaVersion: 1, kind: RECEIPT_KIND, cohort: valid.cohort, planSha256,
    cells: valid.cells.map(toReceiptCell), counts: valid.counts, failedCellIds,
    qualified: failedCellIds.length === 0 && hasQualifiedCounts(valid.counts) };
  validateOpenLoopCohortReceipt(receipt, valid.plan);
  return receipt;
}

function toReceiptCell(item) {
  return { cell: item.cell, disposition: item.terminal.disposition, reason: item.terminal.reason,
    job: item.job, artifact: item.artifact, terminalSha256: item.terminalSha256,
    artifacts: item.terminal.artifacts };
}

export function validateOpenLoopCohortReceipt(value, planValue) {
  const plan = validateOpenLoopPlan(planValue);
  reject(exactKeys(value, CELL_TERMINAL.receiptKeys) && value.schemaVersion === 1
    && value.kind === RECEIPT_KIND && lowercaseHash.test(value.planSha256)
    && Array.isArray(value.cells) && value.cells.length === CELL_TERMINAL.maximumCells
    && Array.isArray(value.failedCellIds) && typeof value.qualified === 'boolean');
  validateCohort(value.cohort);
  const expected = [...plan.measurementCells, ...plan.cancellationProofCells];
  const identities = new Set();
  const jobs = new Set();
  const artifacts = new Set();
  const counts = { plannedMeasurements: CELL_TERMINAL.measurements, plannedProofs: CELL_TERMINAL.proofs,
    measured: 0, cancellationProof: 0, unsupportedTopology: 0, failed: 0 };
  value.cells.forEach((item, index) => validateReceiptCell(item, expected[index], value.cohort,
    identities, jobs, artifacts, counts));
  validateReceiptTotals(value, counts, identities);
  return value;
}

function validateReceiptCell(item, cell, cohort, identities, jobs, artifacts, counts) {
  reject(exactKeys(item, CELL_TERMINAL.receiptCellKeys) && isDeepStrictEqual(item.cell, cell)
    && !identities.has(cell.id) && lowercaseHash.test(item.terminalSha256)
    && Array.isArray(item.artifacts));
  identities.add(cell.id);
  const terminal = { worker: workerFor(cell, cohort, item.job?.id), disposition: item.disposition,
    reason: item.reason, artifacts: item.artifacts };
  validateReceiptDisposition(terminal, cell);
  validateJob(item.job, terminal, cell, cohort, jobs);
  validateArtifact(item.artifact, cell, artifacts);
  validateReceiptDescriptors(item.artifacts, item.disposition, cell);
  counts[item.disposition === 'measured' ? 'measured' : item.disposition === 'cancellationProof'
    ? 'cancellationProof' : item.disposition === 'unsupportedTopology' ? 'unsupportedTopology' : 'failed']++;
}

function validateReceiptTotals(value, counts, identities) {
  const failures = value.cells.filter(item => item.disposition === 'failed').map(item => item.cell.id).sort(ordinal);
  reject(identities.size === CELL_TERMINAL.maximumCells && isDeepStrictEqual(value.counts, counts)
    && exactKeys(value.counts, CELL_TERMINAL.countKeys)
    && isDeepStrictEqual(value.failedCellIds, failures)
    && value.qualified === (failures.length === 0 && hasQualifiedCounts(counts)));
}

function validateReceiptDisposition(terminal, cell) {
  if (terminal.disposition === 'failed') {
    reject(terminal.reason === CELL_TERMINAL.failedReason && terminal.artifacts.length === 0);
    return;
  }
  const unsupported = unsupportedReason(cell);
  if (terminal.disposition === 'unsupportedTopology') {
    reject(!cell.cancellationProof && unsupported !== null && terminal.reason === unsupported
      && terminal.artifacts.length === 2);
    return;
  }
  reject(unsupported === null && terminal.disposition === (cell.cancellationProof ? 'cancellationProof' : 'measured')
    && terminal.reason === null && terminal.artifacts.length === 2);
}

function validateCohort(value) {
  reject(exactKeys(value, CELL_TERMINAL.cohortKeys) && sourceSha.test(value.sourceRevision)
    && positive(value.runId) && positive(value.attempt) && value.repository === CELL_TERMINAL.repository
    && value.ref === CELL_TERMINAL.ref && value.workflow === CELL_TERMINAL.workflow);
  return value;
}

function validateJob(job, terminal, cell, cohort, seen) {
  reject(exactKeys(job, CELL_TERMINAL.jobKeys) && positive(job.id) && job.id === terminal.worker.jobId
    && !seen.has(job.id) && job.name === expectedJobName(cell)
    && job.url === `${JOB_URL}${cohort.runId}/job/${job.id}`
    && [SUCCESS, FAILURE].includes(job.conclusion) && Array.isArray(job.steps) && job.steps.length === STEPS.length);
  const failed = terminal.disposition === 'failed';
  reject((job.conclusion === FAILURE) === failed);
  job.steps.forEach((step, index) => reject(exactKeys(step, CELL_TERMINAL.stepKeys)
    && step.name === STEPS[index] && [SUCCESS, FAILURE].includes(step.conclusion)
    && (index === 1 ? step.conclusion === SUCCESS : step.conclusion === job.conclusion)));
  seen.add(job.id);
}

function validateArtifact(artifact, cell, seen) {
  const prefix = cell.cancellationProof ? CELL_TERMINAL.proofPrefix : CELL_TERMINAL.measurementPrefix;
  reject(exactKeys(artifact, CELL_TERMINAL.artifactKeys) && positive(artifact.id)
    && !seen.has(artifact.id) && artifact.name === prefix + cell.id
    && positive(artifact.sizeInBytes) && artifact.sizeInBytes <= 134_217_728
    && typeof artifact.digest === 'string' && /^sha256:[a-f0-9]{64}$/u.test(artifact.digest)
    && artifact.expired === false);
  seen.add(artifact.id);
}

function validateReceiptDescriptors(items, disposition, cell) {
  const names = disposition === 'failed' ? [] : disposition === 'unsupportedTopology'
    ? [CELL_TERMINAL.genericSidecarFile, CELL_TERMINAL.genericWorkerFile]
    : [cell.cancellationProof ? CELL_TERMINAL.proofFile : CELL_TERMINAL.measurementFile, CELL_TERMINAL.sidecarFile];
  names.sort(ordinal);
  reject(items.length === names.length && items.every((item, index) => exactKeys(item, CELL_TERMINAL.descriptorKeys)
    && item.name === names[index] && positive(item.sizeInBytes)
    && item.sizeInBytes <= descriptorLimit(item.name) && lowercaseHash.test(item.sha256)));
}

function descriptorLimit(name) {
  if (name === CELL_TERMINAL.sidecarFile || name === CELL_TERMINAL.genericSidecarFile) return CELL_TERMINAL.sidecarBytes;
  return name === CELL_TERMINAL.genericWorkerFile ? 67_108_864 : CELL_TERMINAL.reportBytes;
}

function countCells(cells) {
  const counts = { plannedMeasurements: CELL_TERMINAL.measurements, plannedProofs: CELL_TERMINAL.proofs,
    measured: 0, cancellationProof: 0, unsupportedTopology: 0, failed: 0 };
  for (const item of cells) {
    const disposition = item.terminal.disposition;
    counts[disposition === 'unsupportedTopology' ? 'unsupportedTopology' : disposition]++;
  }
  return counts;
}

function hasQualifiedCounts(counts) {
  return counts.measured === CELL_TERMINAL.measurements - CELL_TERMINAL.unsupported
    && counts.unsupportedTopology === CELL_TERMINAL.unsupported
    && counts.cancellationProof === CELL_TERMINAL.proofs && counts.failed === 0;
}

function workerFor(cell, cohort, jobId) {
  return { target: cell.target, nodeCount: cell.nodeCount, scenario: cell.scenario, profile: cell.profile,
    ...cohort, jobId };
}

function expectedJobName(cell) {
  const profile = cell.profile === 'intensive-1k-c16' ? '' : ` / ${cell.profile}`;
  const kind = cell.cancellationProof ? 'cancellation proof' : 'open-loop';
  return `${isolatedJobName(cell, false)}${profile} / ${kind} ${cell.offeredRatePerSecond} ops/s`;
}

function unsupportedReason(cell) {
  return readIsolatedContract().unsupportedTopologies.find(item => item.target === cell.target
    && item.nodeCounts.includes(cell.nodeCount))?.reason ?? null;
}

function ordinal(left, right) { return left < right ? -1 : left > right ? 1 : 0; }
