import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { absolutePath, parseBytes, readBytes } from './aggregate-files.mjs';
import { AGGREGATE } from './aggregate-contracts.mjs';
import { createDirectory, requireDirectory } from './image-bundle-files.mjs';
import { writeCapture, writeJson, readJson } from './isolated-github-files.mjs';
import { downloadArtifact } from './isolated-github-api.mjs';
import { contextForProfile, createGitHubContext } from './isolated-github-context.mjs';
import { GH, requireGitHub } from './isolated-github-contract.mjs';
import { flattenPages, validateWorkflowRun } from './isolated-github-validation.mjs';
import { validateDownloadedArchive } from './isolated-github-stream.mjs';
import { extractNativeEntry, inspectNativeZip } from './isolated-github-zip.mjs';
import { readCellTerminal } from './open-loop-cell-terminal.mjs';
import { CELL_TERMINAL, CELL_TERMINAL_ERRORS } from './open-loop-cell-terminal-contract.mjs';
import { validateCellTerminalForPlan } from './open-loop-cell-terminal-validation.mjs';
import { createOpenLoopIntakeHeader, selectOpenLoopWorkers, projectOpenLoopIntakeCell } from './open-loop-github-intake.mjs';
import { validateOpenLoopPlan } from './open-loop-isolated-plan.mjs';
import { validateOpenLoopCohortInput } from './open-loop-cohort-validation.mjs';

const PLAN_FILE = 'open-loop-isolated-plan.v1.json';
const INTAKE_FILE = 'open-loop-cohort-intake.v1.json';
const WORKER_ROWS = Object.freeze(['jobs-pages.json', 'artifacts-pages.json']);

function parseArguments(argv) {
  requireGitHub(argv.length === 3);
  const values = new Map();
  for (const argument of argv) {
    const matched = /^--(capture|plan|output)=(.+)$/u.exec(argument);
    requireGitHub(matched !== null && !values.has(matched[1]));
    values.set(matched[1], absolutePath(matched[2]));
  }
  requireGitHub(values.size === 3);
  return { capture: values.get('capture'), planPath: values.get('plan'), output: values.get('output') };
}

export async function collectOpenLoopGitHubEvidence(environment = process.env, argv = process.argv.slice(2)) {
  const paths = parseArguments(argv);
  requireDisjoint(paths.capture, paths.output);
  requireDisjoint(paths.planPath, paths.output);
  const context = createGitHubContext(environment, process.platform);
  const capture = await readCapturedRun(paths.capture, context);
  const planBytes = await readBytes(paths.planPath, AGGREGATE.metadataBytes);
  const plan = validateOpenLoopPlan(parseBytes(planBytes));
  const selected = selectOpenLoopWorkers(capture, context, plan);
  await createIntake(paths.output, paths.capture, context, selected, plan, planBytes);
}

function requireDisjoint(left, right) {
  const fromLeft = path.relative(left, right);
  const fromRight = path.relative(right, left);
  requireGitHub((fromLeft === '..' || fromLeft.startsWith(`..${path.sep}`))
    && (fromRight === '..' || fromRight.startsWith(`..${path.sep}`)));
}

async function readCapturedRun(root, context) {
  await requireDirectory(root);
  const workflow = await readJson(path.join(root, 'workflow.json'), GH.metadataBytes);
  const run = await readJson(path.join(root, 'run-attempt.json'), GH.metadataBytes);
  validateWorkflowRun(workflow, run, context.cohort);
  const pages = await Promise.all(WORKER_ROWS.map(name => readJson(path.join(root, name), GH.metadataBytes)));
  return { run, jobs: flattenPages(pages[0], 'jobs'), artifacts: flattenPages(pages[1], 'artifacts') };
}

async function createIntake(output, captureRoot, context, selected, plan, planBytes) {
  await createDirectory(output);
  const cellsRoot = await createDirectory(path.join(output, 'cells'));
  const archivesRoot = await createDirectory(path.join(output, 'archives'));
  const githubRoot = await requireDirectory(captureRoot);
  await writeCapture(path.join(output, PLAN_FILE), planBytes);
  const { intake } = createOpenLoopIntakeHeader(context, plan, planBytes);
  const verifiedCells = [];
  for (const item of selected.selected) {
    const directory = await createDirectory(path.join(cellsRoot, item.cell.id));
    const archiveName = `${item.artifact.name}.zip`;
    const archive = path.join(archivesRoot, archiveName);
    await downloadArtifact(item.artifact, archive, GH.workerZipBytes, context);
    const actual = await validateDownloadedArchive(archive, item.artifact, GH.workerZipBytes);
    const terminal = await collectTerminalFiles({ archive, directory, actual, item, plan, githubRoot, context });
    const projected = projectOpenLoopIntakeCell(item);
    intake.cells.push(projected);
    verifiedCells.push({ cell: item.cell, terminal: terminal.terminal, terminalSha256: terminal.sha256,
      job: projected.job, artifact: projected.artifact });
  }
  validateOpenLoopCohortInput({ plan, cohort: intake.cohort, cells: verifiedCells });
  await writeJson(path.join(output, INTAKE_FILE), intake);
}

async function collectTerminalFiles({ archive, directory, actual, item, plan, githubRoot, context }) {
  requireGitHub(actual.bytes === item.artifact.size_in_bytes && actual.sha256 === item.artifact.digest.slice(7));
  const terminalPath = path.join(directory, CELL_TERMINAL.file);
  const inventory = path.join(githubRoot, `${item.cell.id}-open-loop-zip.txt`);
  const entries = await inspectNativeZip(archive, [CELL_TERMINAL.file], inventory, context);
  await extractNativeEntry(archive, CELL_TERMINAL.file, terminalPath, CELL_TERMINAL.maximumBytes, context);
  const bytes = await readBytes(terminalPath, CELL_TERMINAL.maximumBytes);
  const terminal = parseBytes(bytes);
  const profileContext = contextForProfile(context, item.cell.profile);
  validateCellTerminalForPlan(terminal, plan, item.cell, profileContext.cohort);
  const expected = [CELL_TERMINAL.file, ...terminal.artifacts.map(value => value.name)].sort(ordinal);
  const orderedEntries = [...entries].sort(ordinal);
  requireGitHub(orderedEntries.length === expected.length
    && orderedEntries.every((name, index) => name === expected[index]));
  for (const artifact of terminal.artifacts) {
    await extractNativeEntry(archive, artifact.name, path.join(directory, artifact.name), limitFor(artifact.name), context);
  }
  return await readCellTerminal({ directory, plan, cell: item.cell, cohort: profileContext.cohort });
}

function limitFor(name) {
  if (name === CELL_TERMINAL.sidecarFile || name === CELL_TERMINAL.genericSidecarFile) return CELL_TERMINAL.sidecarBytes;
  return name === CELL_TERMINAL.genericWorkerFile ? GH.workerRawBytes : CELL_TERMINAL.reportBytes;
}

function ordinal(left, right) { return left < right ? -1 : left > right ? 1 : 0; }

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { await collectOpenLoopGitHubEvidence(); }
  catch { process.stderr.write(`${CELL_TERMINAL_ERRORS.cohort}\n`); process.exitCode = 1; }
}
