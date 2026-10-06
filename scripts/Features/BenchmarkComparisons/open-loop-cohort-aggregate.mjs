import { constants, mkdir, open, readdir, rename, rm } from 'node:fs/promises';
import { createHash, randomUUID } from 'node:crypto';
import path from 'node:path';
import { AGGREGATE } from './aggregate-contracts.mjs';
import { absolutePath, assertAbsent, existingPath, readBytes } from './aggregate-files.mjs';
import { parseBytes } from './aggregate-json.mjs';
import { CELL_TERMINAL, CELL_TERMINAL_ERRORS, exactKeys, positive, reject } from './open-loop-cell-terminal-contract.mjs';
import { readCellTerminal } from './open-loop-cell-terminal.mjs';
import { createOpenLoopCohortReceipt } from './open-loop-cohort-validation.mjs';
import { createOpenLoopFairnessValidator } from './open-loop-cohort-fairness.mjs';
import { validateOpenLoopPlan } from './open-loop-isolated-plan.mjs';

const PLAN_FILE = 'open-loop-isolated-plan.v1.json';
const INTAKE_FILE = 'open-loop-cohort-intake.v1.json';
const RECEIPT_FILE = 'open-loop-cohort-receipt.v1.json';
const ARCHIVES = 'archives';
const CELLS = 'cells';
const INTAKE_KIND = 'open-loop-cohort-intake.v1';
const ARCHIVE_LIMIT = 134_217_728;

export async function aggregateOpenLoopCohort({ input, output }) {
  const roots = await validatePaths(input, output);
  const stage = path.join(path.dirname(roots.output), `.open-loop-stage-${randomUUID()}`);
  await mkdir(stage, { mode: 0o700 });
  try {
    const result = await readCohort(roots.input);
    await writeCohort(stage, roots.output, result);
    return result.receipt;
  } catch (primary) {
    try {
      await rm(stage, { recursive: true, force: true });
    } catch (cleanup) {
      throw new AggregateError([primary, cleanup], CELL_TERMINAL_ERRORS.output);
    }
    throw primary;
  }
}

async function validatePaths(inputValue, outputValue) {
  const input = absolutePath(inputValue, CELL_TERMINAL_ERRORS.input);
  const output = absolutePath(outputValue, CELL_TERMINAL_ERRORS.output);
  await existingPath(input, true, CELL_TERMINAL_ERRORS.input);
  await existingPath(path.dirname(output), true, CELL_TERMINAL_ERRORS.output);
  const inputFromOutput = path.relative(input, output);
  const outputFromInput = path.relative(output, input);
  reject((inputFromOutput === '..' || inputFromOutput.startsWith(`..${path.sep}`))
    && (outputFromInput === '..' || outputFromInput.startsWith(`..${path.sep}`)), CELL_TERMINAL_ERRORS.output);
  await assertAbsent(output);
  return { input, output };
}

async function readCohort(input) {
  const rootEntries = await readdir(input);
  reject(rootEntries.length === 4 && ['archives', 'cells', PLAN_FILE, INTAKE_FILE].every(name => rootEntries.includes(name)));
  const planBytes = await readBytes(path.join(input, PLAN_FILE), AGGREGATE.metadataBytes);
  const intakeBytes = await readBytes(path.join(input, INTAKE_FILE), AGGREGATE.metadataBytes);
  const plan = validateOpenLoopPlan(parseBytes(planBytes));
  const intake = parseBytes(intakeBytes);
  validateIntakeHeader(intake);
  const expected = [...plan.measurementCells, ...plan.cancellationProofCells];
  await validateRootInventories(input, expected);
  const cells = [];
  const checkFairness = createOpenLoopFairnessValidator();
  let retainedBytes = planBytes.length + intakeBytes.length;
  for (let index = 0; index < expected.length; index++) {
    const cell = expected[index];
    const metadata = intake.cells[index];
    validateIntakeCellIdentity(metadata, cell);
    const directory = path.join(input, CELLS, cell.id);
    await validateCellInventory(directory, cell);
    const loaded = await readCellTerminal({ directory, plan, cell, cohort: intake.cohort });
    retainedBytes = addBytes(retainedBytes, loaded.bytes.length);
    const files = loaded.files;
    for (const descriptor of loaded.terminal.artifacts) {
      const bytes = await readBytes(path.join(directory, descriptor.name), descriptorLimit(descriptor.name));
      retainedBytes = addBytes(retainedBytes, bytes.length);
    }
    const archiveName = `${metadata.artifact.name}.zip`;
    const archive = await readBytes(path.join(input, ARCHIVES, archiveName), ARCHIVE_LIMIT);
    retainedBytes = addBytes(retainedBytes, archive.length);
    reject(archive.length === metadata.artifact.sizeInBytes
      && `sha256:${hashBytes(archive)}` === metadata.artifact.digest);
    cells.push({ cell, terminal: loaded.terminal, terminalBytes: loaded.bytes, terminalSha256: loaded.sha256,
      job: metadata.job, artifact: metadata.artifact, archiveName });
    if (loaded.terminal.disposition === 'measured') {
      checkFairness({ cell, disposition: 'measured', report: files[CELL_TERMINAL.measurementFile],
        sidecar: files[CELL_TERMINAL.sidecarFile] });
    }
    reject(retainedBytes <= CELL_TERMINAL.totalBytes, CELL_TERMINAL_ERRORS.cohort);
  }
  await validateArchiveInventory(path.join(input, ARCHIVES), cells.map(item => item.archiveName));
  const cohortInput = { plan, cohort: intake.cohort, cells: cells.map(({ cell, terminal, terminalSha256, job, artifact }) =>
    ({ cell, terminal, terminalSha256, job, artifact })) };
  const receipt = createOpenLoopCohortReceipt(cohortInput, hashBytes(planBytes));
  return { inputRoot: input, planBytes, cells, receipt, retainedBytes };
}

function validateIntakeHeader(value) {
  reject(exactKeys(value, ['schemaVersion', 'kind', 'cohort', 'cells'])
    && value.schemaVersion === 1 && value.kind === INTAKE_KIND
    && Array.isArray(value.cells) && value.cells.length === CELL_TERMINAL.maximumCells);
}

function validateIntakeCellIdentity(value, cell) {
  const prefix = cell.cancellationProof ? CELL_TERMINAL.proofPrefix : CELL_TERMINAL.measurementPrefix;
  reject(exactKeys(value, ['id', 'job', 'artifact']) && value.id === cell.id
    && exactKeys(value.artifact, CELL_TERMINAL.artifactKeys) && positive(value.artifact.id)
    && value.artifact.name === prefix + cell.id && positive(value.artifact.sizeInBytes)
    && value.artifact.sizeInBytes <= ARCHIVE_LIMIT
    && typeof value.artifact.digest === 'string' && /^sha256:[a-f0-9]{64}$/u.test(value.artifact.digest)
    && value.artifact.expired === false);
}

async function validateRootInventories(input, cells) {
  reject(cells.length === CELL_TERMINAL.maximumCells);
  const cellRoot = path.join(input, CELLS);
  const archiveRoot = path.join(input, ARCHIVES);
  await existingPath(cellRoot, true, CELL_TERMINAL_ERRORS.input);
  await existingPath(archiveRoot, true, CELL_TERMINAL_ERRORS.input);
  const directories = await readdir(cellRoot, { withFileTypes: true });
  const names = directories.map(entry => entry.name);
  reject(directories.length === cells.length && directories.every(entry => entry.isDirectory()
    && !entry.isSymbolicLink()) && cells.every(cell => names.includes(cell.id)));
}

async function validateCellInventory(directory, cell) {
  const entries = (await readdir(directory, { withFileTypes: true }))
    .sort((left, right) => left.name < right.name ? -1 : left.name > right.name ? 1 : 0);
  const terminal = path.basename(CELL_TERMINAL.file);
  const expected = [terminal, ...(await readTerminalNames(directory))].sort();
  reject(entries.length === expected.length && entries.every((entry, index) => entry.isFile()
    && !entry.isSymbolicLink() && entry.name === expected[index]));
  for (const name of expected) await existingPath(path.join(directory, name), false, CELL_TERMINAL_ERRORS.input);
}

async function readTerminalNames(directory) {
  const terminal = await readBytes(path.join(directory, CELL_TERMINAL.file), CELL_TERMINAL.maximumBytes);
  const value = parseBytes(terminal);
  reject(Array.isArray(value.artifacts));
  return value.artifacts.map(item => item.name);
}

async function validateArchiveInventory(root, expectedNames) {
  const entries = await readdir(root);
  reject(entries.length === expectedNames.length && expectedNames.every(name => entries.includes(name))
    && new Set(expectedNames).size === expectedNames.length);
  for (const name of expectedNames) await existingPath(path.join(root, name), false, CELL_TERMINAL_ERRORS.input);
}

async function writeCohort(stage, output, result) {
  await writeOwned(path.join(stage, RECEIPT_FILE), jsonBytes(result.receipt));
  await writeOwned(path.join(stage, PLAN_FILE), result.planBytes);
  for (const item of result.cells) {
    const cellRoot = path.join(stage, CELLS, item.cell.id);
    await mkdir(cellRoot, { recursive: true, mode: 0o700 });
    await writeOwned(path.join(cellRoot, CELL_TERMINAL.file), item.terminalBytes);
    for (const descriptor of item.terminal.artifacts) {
      const source = path.join(result.inputRoot, CELLS, item.cell.id, descriptor.name);
      const bytes = await readBytes(source, descriptorLimit(descriptor.name));
      reject(bytes.length === descriptor.sizeInBytes && hashBytes(bytes) === descriptor.sha256);
      await writeOwned(path.join(cellRoot, descriptor.name), bytes);
    }
    const archiveRoot = path.join(stage, ARCHIVES);
    await mkdir(archiveRoot, { recursive: true, mode: 0o700 });
    const archive = await readBytes(path.join(result.inputRoot, ARCHIVES, item.archiveName), ARCHIVE_LIMIT);
    reject(archive.length === item.artifact.sizeInBytes && `sha256:${hashBytes(archive)}` === item.artifact.digest);
    await writeOwned(path.join(archiveRoot, item.archiveName), archive);
  }
  await assertAbsent(output);
  await rename(stage, output);
}

function jsonBytes(value) {
  const bytes = Buffer.from(`${JSON.stringify(value, null, 2)}\n`, 'utf8');
  reject(bytes.length <= AGGREGATE.metadataBytes, CELL_TERMINAL_ERRORS.output);
  return bytes;
}

async function writeOwned(target, bytes) {
  const handle = await open(target, constants.O_WRONLY | constants.O_CREAT | constants.O_EXCL
    | (constants.O_NOFOLLOW ?? 0), 0o600);
  const failures = [];
  try {
    await handle.writeFile(bytes);
    await handle.sync();
    await handle.chmod(0o400);
  } catch (error) {
    failures.push(error);
  }
  try {
    await handle.close();
  } catch (error) {
    failures.push(error);
  }
  if (failures.length === 1) throw failures[0];
  if (failures.length > 1) throw new AggregateError(failures, CELL_TERMINAL_ERRORS.output);
}

function descriptorLimit(name) {
  if (name === CELL_TERMINAL.genericWorkerFile) return AGGREGATE.workerBytes;
  if (name === CELL_TERMINAL.sidecarFile || name === CELL_TERMINAL.genericSidecarFile) return CELL_TERMINAL.sidecarBytes;
  return CELL_TERMINAL.reportBytes;
}

function hashBytes(bytes) { return createHash('sha256').update(bytes).digest('hex'); }
function addBytes(current, addition) {
  const total = current + addition;
  reject(Number.isSafeInteger(total) && total <= CELL_TERMINAL.totalBytes, CELL_TERMINAL_ERRORS.cohort);
  return total;
}
