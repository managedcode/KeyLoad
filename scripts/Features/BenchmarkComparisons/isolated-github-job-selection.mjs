import { contextForProfile } from './isolated-github-context.mjs';
import { createDatabaseMatrices } from './isolated-preflight.mjs';
import { GH, requireGitHub } from './isolated-github-contract.mjs';

const jobNameSelector = 'KEYLOAD_COMPARISON_JOB_NAME';
const cellIdSelector = 'KEYLOAD_COMPARISON_CELL_ID';
const evidenceProfileSelector = 'Benchmarks__EvidenceProfile';
const scaleProfileSelector = 'KEYLOAD_SCALE_PROFILE';
const vectorProfileSelector = 'KEYLOAD_VECTOR_PROFILE';
const openLoopRateSelector = 'KEYLOAD_OPEN_LOOP_RATE';
const openLoopProofSelector = 'KEYLOAD_OPEN_LOOP_CANCELLATION_PROOF';
const nodeCountSelector = 'Benchmarks__NodeCount';
const scenarioSelector = 'Benchmarks__Scenario';
const vectorEvidenceProfileSelector = 'Benchmarks__VectorProfile';
const targetSelector = 'Benchmarks__Target';
const matrixKindSelector = 'KEYLOAD_MATRIX_KIND';
const workloadSelectors = Object.freeze([
  cellIdSelector,
  evidenceProfileSelector,
  scaleProfileSelector,
  vectorProfileSelector,
  openLoopRateSelector,
  openLoopProofSelector,
  nodeCountSelector,
  scenarioSelector,
  vectorEvidenceProfileSelector,
  targetSelector,
  matrixKindSelector,
]);

function selectedKind(row) {
  if (row.preflight) return 'preflight';
  if (row.openLoopCancellationProof) return 'proof';
  return row.openLoopRate === undefined ? 'worker' : 'open-loop';
}

function validateOptionalRowSelectors(environment, row) {
  const expected = new Map([
    [scaleProfileSelector, row.scaleProfile ?? ''],
    [vectorProfileSelector, row.vectorProfile ?? ''],
    [openLoopRateSelector, row.openLoopRate === undefined ? '' : String(row.openLoopRate)],
    [openLoopProofSelector, row.openLoopCancellationProof === undefined ? '' : String(row.openLoopCancellationProof)],
    [nodeCountSelector, String(row.nodeCount)],
    [scenarioSelector, row.scenario],
    [vectorEvidenceProfileSelector, row.vectorProfile ?? ''],
    [targetSelector, row.target],
    [matrixKindSelector, selectedKind(row)],
  ]);

  for (const [selector, value] of expected) {
    requireGitHub(Object.hasOwn(environment, selector) && environment[selector] === value);
  }
}

export function selectCurrentJob(environment, context) {
  requireGitHub(environment !== null && typeof environment === 'object'
    && context !== null && typeof context === 'object');

  const name = environment[jobNameSelector];
  requireGitHub(typeof name === 'string' && name.length > 0 && name.trim() === name);

  const hasCellId = Object.hasOwn(environment, cellIdSelector);
  const hasEvidenceProfile = Object.hasOwn(environment, evidenceProfileSelector);
  if (name === GH.imageJob) {
    requireGitHub(workloadSelectors.every(selector => !Object.hasOwn(environment, selector)));
    return Object.freeze({ name, context });
  }

  requireGitHub(hasCellId && hasEvidenceProfile);
  const cellId = environment[cellIdSelector];
  const profile = environment[evidenceProfileSelector];
  requireGitHub(typeof cellId === 'string' && cellId.length > 0 && cellId.trim() === cellId
    && typeof profile === 'string' && profile.length > 0 && profile.trim() === profile);

  const rows = Object.values(createDatabaseMatrices(context.plan, context.scaledPlans,
    context.vectorPlans, context.openLoopPlan)).flatMap(matrix => matrix.include);
  const matches = rows.filter(row => row.id === cellId && row.jobName === name && row.profile === profile);
  requireGitHub(matches.length === 1);
  validateOptionalRowSelectors(environment, matches[0]);

  return Object.freeze({ name, context: contextForProfile(context, matches[0].profile) });
}
