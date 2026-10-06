import { AGGREGATE } from './aggregate-contracts.mjs';

export const CELL_TERMINAL = Object.freeze({
  file: 'open-loop-cell-terminal.v1.json',
  kind: 'open-loop-cell-terminal.v1',
  version: 1,
  maximumBytes: 65_536,
  reportBytes: 4_194_304,
  sidecarBytes: 65_536,
  planned: 100_000,
  sampleCapacity: 4_096,
  payloadBytes: 1_024,
  rates: Object.freeze([250, 1_000, 4_000]),
  measurements: 792,
  proofs: 6,
  unsupported: 144,
  maximumCells: 798,
  totalBytes: AGGREGATE.totalBytes,
  failedReason: AGGREGATE.failureReason,
  repository: AGGREGATE.repository,
  ref: AGGREGATE.ref,
  workflow: AGGREGATE.workflow,
  measurementFile: 'open-loop-evidence.v1.json',
  proofFile: 'open-loop-cancellation-proof.v1.json',
  sidecarFile: 'open-loop-server-resource-evidence.v1.json',
  genericWorkerFile: 'worker.json',
  genericSidecarFile: 'server-resource-evidence.json',
  measurementPrefix: 'comparison-open-loop-worker-',
  proofPrefix: 'comparison-open-loop-proof-',
  measurementSteps: AGGREGATE.steps,
  terminalKeys: Object.freeze(['schemaVersion', 'kind', 'cell', 'worker', 'disposition', 'reason', 'artifacts']),
  cellKeys: Object.freeze(['id', 'target', 'nodeCount', 'scenario', 'profile', 'family', 'offeredRatePerSecond', 'cancellationProof']),
  workerKeys: Object.freeze(['target', 'nodeCount', 'scenario', 'profile', 'sourceRevision', 'runId', 'attempt', 'repository', 'ref', 'workflow', 'jobId']),
  descriptorKeys: Object.freeze(['name', 'sizeInBytes', 'sha256']),
  receiptKeys: Object.freeze(['schemaVersion', 'kind', 'cohort', 'planSha256', 'cells', 'counts', 'failedCellIds', 'qualified']),
  cohortKeys: Object.freeze(['sourceRevision', 'runId', 'attempt', 'repository', 'ref', 'workflow']),
  receiptCellKeys: Object.freeze(['cell', 'disposition', 'reason', 'job', 'artifact', 'terminalSha256', 'artifacts']),
  countKeys: Object.freeze(['plannedMeasurements', 'plannedProofs', 'measured', 'cancellationProof', 'unsupportedTopology', 'failed']),
  jobKeys: Object.freeze(['id', 'name', 'url', 'conclusion', 'steps']),
  stepKeys: Object.freeze(['name', 'conclusion']),
  artifactKeys: Object.freeze(['id', 'name', 'sizeInBytes', 'digest', 'expired']),
});

export const CELL_TERMINAL_ERRORS = Object.freeze({
  input: 'Open-loop terminal input is invalid.',
  output: 'Open-loop terminal output is invalid.',
  cohort: 'Open-loop cohort evidence is invalid.',
});

export const exactKeys = (value, keys) => value !== null && typeof value === 'object' && !Array.isArray(value)
  && Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
export const positive = value => Number.isSafeInteger(value) && value > 0;
export const nonnegative = value => Number.isSafeInteger(value) && value >= 0;
export const finite = value => typeof value === 'number' && Number.isFinite(value);
export const lowercaseHash = /^[a-f0-9]{64}$/u;
export const sourceSha = /^[a-f0-9]{40}$/u;
export const safeCellId = /^[a-z0-9]+(?:-[a-z0-9]+)*$/u;

export function reject(condition, category = CELL_TERMINAL_ERRORS.input) {
  if (!condition) {
    const error = new Error(category);
    error.code = 'E_OPEN_LOOP_TERMINAL';
    throw error;
  }
}
