import { createHash } from 'node:crypto';
import path from 'node:path';
import { parseBytes } from './aggregate-json.mjs';
import { readBytes } from './aggregate-files.mjs';
import { writeCapture } from './isolated-github-files.mjs';
import { requireDirectory } from './image-bundle-files.mjs';
import { validateWorkerEnvelope } from './aggregate-validation.mjs';
import { validateServerResourceEvidence } from './server-resource-evidence.mjs';
import { readIsolatedContract } from './isolated-plan.mjs';
import { validateOpenLoopPlan } from './open-loop-isolated-plan.mjs';
import { CELL_TERMINAL, CELL_TERMINAL_ERRORS, reject } from './open-loop-cell-terminal-contract.mjs';
import { validateCanonicalCellIdentity, validateCellTerminalForPlan, validateNativeArtifact, validateOpenLoopSidecar } from './open-loop-cell-terminal-validation.mjs';

const TERMINAL = CELL_TERMINAL.file;
const SHA256 = 'sha256';
const GENERIC_CONTRACT = readIsolatedContract();

export async function publishCellTerminal({ directory, plan: inputPlan, cell: inputCell, worker, disposition, reason }) {
  const root = await requireDirectory(directory);
  const plan = validateOpenLoopPlan(inputPlan);
  const cell = validateCanonicalCellIdentity(plan, inputCell);
  const terminal = await createTerminal(root, plan, cell, worker, disposition, reason);
  const bytes = Buffer.from(`${JSON.stringify(terminal)}\n`, 'utf8');
  reject(bytes.length <= CELL_TERMINAL.maximumBytes, CELL_TERMINAL_ERRORS.output);
  await writeCapture(path.join(root, TERMINAL), bytes);
  return terminal;
}

export async function readCellTerminal({ directory, plan: inputPlan, cell: inputCell, cohort = null }) {
  const root = await requireDirectory(directory);
  const plan = validateOpenLoopPlan(inputPlan);
  const cell = validateCanonicalCellIdentity(plan, inputCell);
  const bytes = await readBytes(path.join(root, TERMINAL), CELL_TERMINAL.maximumBytes);
  const terminal = parseBytes(bytes);
  validateCellTerminalForPlan(terminal, plan, cell, cohort);
  const files = await validateTerminalFiles(root, terminal, cell);
  return { terminal, bytes, files, sha256: createHash(SHA256).update(bytes).digest('hex') };
}

async function createTerminal(directory, plan, cell, worker, disposition, reason) {
  const artifacts = disposition === 'failed' ? [] : await readDisposition(directory, cell, worker, disposition, reason);
  const terminal = { schemaVersion: CELL_TERMINAL.version, kind: CELL_TERMINAL.kind,
    cell, worker, disposition, reason, artifacts };
  validateCellTerminalForPlan(terminal, plan, cell);
  return terminal;
}

async function readDisposition(directory, cell, worker, disposition, reason) {
  if (disposition === 'unsupportedTopology') {
    return await readUnsupported(directory, cell, worker, reason);
  }
  const expectedDisposition = cell.cancellationProof ? 'cancellationProof' : 'measured';
  reject(disposition === expectedDisposition && reason === null);
  const rawName = cell.cancellationProof ? CELL_TERMINAL.proofFile : CELL_TERMINAL.measurementFile;
  const raw = await readJsonArtifact(directory, rawName, CELL_TERMINAL.reportBytes);
  validateNativeArtifact(raw.value, cell, worker);
  const sidecar = await readJsonArtifact(directory, CELL_TERMINAL.sidecarFile, CELL_TERMINAL.sidecarBytes);
  const kind = cell.cancellationProof ? 'cancellation-proof' : 'measurement';
  validateOpenLoopSidecar(sidecar.value, raw.sha256, sidecar.sha256, cell, worker, kind, rawName);
  return sortDescriptors([raw.descriptor, sidecar.descriptor]);
}

async function readUnsupported(directory, cell, worker, reason) {
  const expected = unsupportedReason(cell);
  reject(!cell.cancellationProof && expected !== null && reason === expected);
  const raw = await readJsonArtifact(directory, CELL_TERMINAL.genericWorkerFile, 67_108_864);
  const cohort = workerCohort(worker, cell.profile);
  const baseCell = genericCell(cell);
  const contract = { ...GENERIC_CONTRACT, profile: cell.profile };
  const envelope = validateWorkerEnvelope(raw.value, baseCell, cohort, contract);
  reject(envelope.disposition === 'unsupportedTopology' && envelope.reason === expected);
  const sidecar = await readJsonArtifact(directory, CELL_TERMINAL.genericSidecarFile, CELL_TERMINAL.sidecarBytes);
  validateServerResourceEvidence(sidecar.value, sidecar.sha256, raw.sha256, baseCell, cohort, worker.jobId, false);
  return sortDescriptors([raw.descriptor, sidecar.descriptor]);
}

async function validateTerminalFiles(directory, terminal, cell) {
  if (terminal.disposition === 'failed') {
    reject(terminal.artifacts.length === 0);
    return Object.create(null);
  }
  const entries = new Map();
  const values = Object.create(null);
  for (const expected of terminal.artifacts) {
    const actual = await readJsonArtifact(directory, expected.name, limitFor(expected.name));
    reject(actual.descriptor.sizeInBytes === expected.sizeInBytes && actual.sha256 === expected.sha256);
    entries.set(expected.name, actual);
    values[expected.name] = actual.value;
  }
  if (terminal.disposition === 'unsupportedTopology') {
    const worker = entries.get(CELL_TERMINAL.genericWorkerFile);
    const cohort = workerCohort(terminal.worker, cell.profile);
    const baseCell = genericCell(cell);
    const envelope = validateWorkerEnvelope(worker.value, baseCell, cohort,
      { ...GENERIC_CONTRACT, profile: cell.profile });
    const sidecar = entries.get(CELL_TERMINAL.genericSidecarFile);
    validateServerResourceEvidence(sidecar.value, sidecar.sha256, worker.sha256, baseCell, cohort,
      terminal.worker.jobId, false);
    reject(envelope.reason === terminal.reason);
    return values;
  }
  const rawName = cell.cancellationProof ? CELL_TERMINAL.proofFile : CELL_TERMINAL.measurementFile;
  const raw = entries.get(rawName);
  validateNativeArtifact(raw.value, cell, terminal.worker);
  validateOpenLoopSidecar(entries.get(CELL_TERMINAL.sidecarFile).value, raw.sha256,
    entries.get(CELL_TERMINAL.sidecarFile).sha256, cell,
    terminal.worker, cell.cancellationProof ? 'cancellation-proof' : 'measurement', rawName);
  return values;
}

async function readJsonArtifact(directory, name, maximumBytes) {
  const bytes = await readBytes(path.join(directory, name), maximumBytes);
  const value = parseBytes(bytes);
  return { value, bytes, sha256: createHash(SHA256).update(bytes).digest('hex'),
    descriptor: { name, sizeInBytes: bytes.length, sha256: createHash(SHA256).update(bytes).digest('hex') } };
}

function workerCohort(worker, profile) {
  return { sourceRevision: worker.sourceRevision, runId: worker.runId, attempt: worker.attempt,
    repository: worker.repository, ref: worker.ref, workflow: worker.workflow, profile };
}

function genericCell(cell) {
  return { id: cell.id, target: cell.target, nodeCount: cell.nodeCount,
    scenario: cell.scenario, profile: cell.profile, family: 'scaled' };
}

function unsupportedReason(cell) {
  return GENERIC_CONTRACT.unsupportedTopologies.find(item => item.target === cell.target
    && item.nodeCounts.includes(cell.nodeCount))?.reason ?? null;
}

function limitFor(name) {
  if (name === CELL_TERMINAL.genericWorkerFile) return 67_108_864;
  return name === CELL_TERMINAL.sidecarFile || name === CELL_TERMINAL.genericSidecarFile
    ? CELL_TERMINAL.sidecarBytes : CELL_TERMINAL.reportBytes;
}

function sortDescriptors(items) {
  return items.sort((left, right) => left.name < right.name ? -1 : left.name > right.name ? 1 : 0);
}
