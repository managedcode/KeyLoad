import { createDocumentPlan, validateDocumentPlan, documentMatrixRow } from './document-isolated-plan.mjs';
import { constants } from 'node:fs';
import { open, readdir } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { isDeepStrictEqual } from 'node:util';
import { validateIsolatedPlan } from './isolated-plan.mjs';
import { validateScaledPlans, createCompositePlan } from './scaled-isolated-plan.mjs';
import { validateVectorPlans } from './vector-isolated-plan.mjs';
import { validateOpenLoopPlan } from './open-loop-isolated-plan.mjs';
import { createDatabaseMatrices } from './isolated-preflight.mjs';
import { GH, requireGitHub } from './isolated-github-contract.mjs';
import { existingPath } from './aggregate-files.mjs';
import { readJson } from './isolated-github-files.mjs';

const MATRIX = Object.freeze({
  directory: 'KEYLOAD_MATRIX_PLAN_DIRECTORY', id: 'KEYLOAD_COMPARISON_CELL_ID',
  jobName: 'KEYLOAD_COMPARISON_JOB_NAME', target: 'Benchmarks__Target', kind: 'KEYLOAD_MATRIX_KIND',
  files: ['isolated-plan.json', 'scaled-plan.json', 'vector-plan.json', 'composite-plan.json', 'open-loop-plan.json', 'document-plan.json'],
  output: 'GITHUB_ENV', selectors: ['KEYLOAD_SCALE_PROFILE', 'KEYLOAD_VECTOR_PROFILE', 'KEYLOAD_OPEN_LOOP_RATE',
    'KEYLOAD_OPEN_LOOP_CANCELLATION_PROOF', 'Benchmarks__NodeCount', 'Benchmarks__Scenario',
    'Benchmarks__EvidenceProfile', 'Benchmarks__VectorProfile', 'Benchmarks__DocumentScenario', 'Benchmarks__DocumentRecords', 'Benchmarks__DocumentClients'],
});

async function readCanonicalPlans(directory) {
  const names = (await readdir(directory)).sort();
  requireGitHub(isDeepStrictEqual(names, [...MATRIX.files].sort()));
  const values = await Promise.all(MATRIX.files.map(name => readJson(path.join(directory, name), GH.metadataBytes)));
  const [plan, scales, vectors, composite, openLoop, documents] = values;
  const canonicalPlan = validateIsolatedPlan(plan);
  const canonicalScales = validateScaledPlans(scales);
  const canonicalVectors = validateVectorPlans(vectors);
  const canonicalOpenLoop = validateOpenLoopPlan(openLoop);
  requireGitHub(isDeepStrictEqual(composite, createCompositePlan(canonicalPlan, canonicalScales, canonicalVectors)));
  return { plan: canonicalPlan, scales: canonicalScales, vectors: canonicalVectors, openLoop: canonicalOpenLoop, documents: validateDocumentPlan(documents) };
}

function selectedKind(row) {
  if (row.family === 'document-v1') return 'documents';
  if (row.preflight) return 'preflight';
  if (row.openLoopCancellationProof) return 'proof';
  return row.openLoopRate === undefined ? 'worker' : 'open-loop';
}

function findSelectedRow(matrices, environment) {
  const rows = Object.values(matrices).flatMap(matrix => matrix.include);
  const matches = rows.filter(row => row.id === environment[MATRIX.id] && row.target === environment[MATRIX.target]
    && row.jobName === environment[MATRIX.jobName] && selectedKind(row) === environment[MATRIX.kind]);
  requireGitHub(matches.length === 1);
  return matches[0];
}

function selectorValues(row) {
  const scale = row.scaleProfile ?? '';
  const vector = row.vectorProfile ?? '';
  return [scale, vector, row.openLoopRate === undefined ? '' : String(row.openLoopRate),
    row.openLoopCancellationProof === undefined ? '' : String(row.openLoopCancellationProof),
    String(row.nodeCount), row.scenario, row.profile, vector, row.documentScenario ?? '',
    row.documentRecords === undefined ? '' : String(row.documentRecords), row.documentClients === undefined ? '' : String(row.documentClients)];
}

async function appendEnvironment(pathname, values) {
  const before = await existingPath(pathname, false);
  const handle = await open(pathname, constants.O_WRONLY | constants.O_APPEND | (constants.O_NOFOLLOW ?? 0));
  try {
    const opened = await handle.stat();
    requireGitHub(opened.dev === before.dev && opened.ino === before.ino && opened.size === before.size);
    await handle.writeFile(Buffer.from(MATRIX.selectors.map((key, index) => `${key}=${values[index]}\n`).join(''), 'utf8'));
    await handle.sync();
    requireGitHub(opened.dev === (await handle.stat()).dev && opened.ino === (await handle.stat()).ino);
  } finally {
    await handle.close();
  }
}

export async function resolveMatrixEntry(environment = process.env) {
  const directory = environment[MATRIX.directory];
  requireGitHub(typeof directory === 'string' && path.isAbsolute(directory));
  await existingPath(directory, true);
  const source = await readCanonicalPlans(directory);
  const matrices = createDatabaseMatrices(source.plan, source.scales, source.vectors, source.openLoop, source.documents.cells.map(documentMatrixRow));
  const row = findSelectedRow(matrices, environment);
  await appendEnvironment(environment[MATRIX.output], selectorValues(row));
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try { await resolveMatrixEntry(); } catch {
    process.stderr.write(`${GH.failure}\n`);
    process.exitCode = 1;
  }
}
