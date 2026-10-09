import { isDeepStrictEqual } from 'node:util';
import { createDocumentPlan, documentMatrixRow } from './document-isolated-plan.mjs';
import { validateIsolatedPlan } from './isolated-plan.mjs';
import { isolatedPlanLimits, requireIsolatedPlan } from './isolated-plan-contract.mjs';
import { createScaledPlans, validateScaledPlans } from './scaled-isolated-plan.mjs';
import { createVectorPlans, validateVectorPlans } from './vector-isolated-plan.mjs';
import { validateOpenLoopPlan } from './open-loop-isolated-plan.mjs';
import { isolatedJobLabel, isolatedJobName } from '../../../site/Features/BenchmarkComparisons/isolated-contracts.mjs';

const representatives = Object.freeze({ Qdrant: 'VectorExact', RabbitMQ: 'QueueCycle', KurrentDB: 'StreamAppend' });
const databaseKeys = Object.freeze({ KeyLoad: 'keyload', 'PostgreSQL + pgvector': 'postgresql',
  Qdrant: 'qdrant', RabbitMQ: 'rabbitmq', Redis: 'redis', Neo4j: 'neo4j', MongoDB: 'mongodb',
  OpenSearch: 'opensearch', KurrentDB: 'kurrentdb', SurrealDB: 'surrealdb', HelixDB: 'helixdb' });

export function createPreflightMatrix(plan) {
  const canonical = validateIsolatedPlan(plan);
  const include = canonical.cells.filter(cell => cell.scenario === (representatives[cell.target] ?? 'PointRead'));
  requireIsolatedPlan(include.length === 22 && new Set(include.map(cell => cell.id)).size === 22);
  return { include };
}

function databaseRow(cell, preflight) {
  const baseLabel = isolatedJobLabel(cell, preflight);
  const profileLabel = !preflight && cell.profile !== 'intensive-1k-c16' ? ` / ${cell.profile}` : '';
  const scaled = !preflight && cell.profile.startsWith('scaled-');
  const vector = !preflight && cell.profile.startsWith('vector-');
  return { ...cell, preflight, label: baseLabel + profileLabel, jobName: isolatedJobName(cell, preflight) + profileLabel,
    scaleProfile: scaled ? cell.profile : null,
    vectorProfile: vector ? cell.profile : null,
    artifactPrefix: preflight ? 'comparison-preflight-' : 'comparison-worker-',
    qualificationPrefix: preflight ? 'comparison-preflight-qualification-' : 'comparison-case-qualification-' };
}

function openLoopDatabaseRow(cell) {
  const original = databaseRow(cell, false);
  const kind = cell.cancellationProof ? 'cancellation proof' : 'open-loop';
  const suffix = ` / ${kind} ${cell.offeredRatePerSecond} ops/s`;
  return { ...original, label: original.label + suffix, jobName: original.jobName + suffix,
    openLoopRate: cell.offeredRatePerSecond,
    openLoopCancellationProof: cell.cancellationProof,
    artifactPrefix: cell.cancellationProof ? 'comparison-open-loop-proof-' : 'comparison-open-loop-worker-',
    qualificationPrefix: cell.cancellationProof ? 'comparison-open-loop-proof-qualification-' : 'comparison-open-loop-case-qualification-' };
}

export function createDatabaseMatrices(plan, scaledPlans = createScaledPlans(), vectorPlans = createVectorPlans(), openLoopPlan, documentRows = []) {
  requireIsolatedPlan(Array.isArray(documentRows) && (documentRows.length === 0
    || isDeepStrictEqual(documentRows, createDocumentPlan().cells.map(documentMatrixRow))));
  const canonical = validateIsolatedPlan(plan);
  const scales = validateScaledPlans(scaledPlans);
  const vectors = validateVectorPlans(vectorPlans);
  const openLoop = openLoopPlan === undefined ? null : validateOpenLoopPlan(openLoopPlan);
  const checks = createPreflightMatrix(canonical).include;
  requireIsolatedPlan(new Set(canonical.cells.map(cell => cell.target)).size === Object.keys(databaseKeys).length);
  return Object.fromEntries(Object.entries(databaseKeys).map(([target, key]) => {
    const original = [...checks.filter(cell => cell.target === target).map(cell => databaseRow(cell, true)),
      ...canonical.cells.filter(cell => cell.target === target).map(cell => databaseRow(cell, false)),
      ...scales.flatMap(profile => profile.cells.filter(cell => cell.target === target).map(cell => databaseRow(cell, false))),
      ...vectors.flatMap(profile => profile.cells.filter(cell => cell.target === target).map(cell => databaseRow(cell, false)))];
    const measurements = openLoop?.measurementCells.filter(cell => cell.target === target) ?? [];
    const proofs = openLoop?.cancellationProofCells.filter(cell => cell.target === target) ?? [];
    const include = [...original, ...measurements.map(openLoopDatabaseRow), ...proofs.map(openLoopDatabaseRow), ...documentRows.filter(row => row.target === target)];
    const expectedCount = (openLoop === null ? 86 : target === 'KeyLoad' ? 140 : 134) + documentRows.filter(row => row.target === target).length;
    requireIsolatedPlan(original.length === 86 && include.length === expectedCount && include.length <= isolatedPlanLimits.matrix
      && new Set(include.map(cell => cell.jobName)).size === include.length
      && include.every(row => row.vectorProfile === null || row.scaleProfile === null));
    return [key, { include }];
  }));
}

export function createWorkflowDatabaseMatrices(plan, scaledPlans, vectorPlans, openLoopPlan, documentRows = []) {
  const matrices = createDatabaseMatrices(plan, scaledPlans, vectorPlans, openLoopPlan, documentRows);
  return Object.fromEntries(Object.entries(matrices).map(([key, matrix]) => [key, {
    include: matrix.include.map(row => ({ id: row.id, jobName: row.jobName, target: row.target, kind: matrixKind(row) }))
  }]));
}

function matrixKind(row) {
  if (row.family === 'document-v1') return 'documents';
  if (row.preflight) return 'preflight';
  if (row.openLoopCancellationProof) return 'proof';
  return row.openLoopRate === undefined ? 'worker' : 'open-loop';
}
