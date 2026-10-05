import { validateIsolatedPlan } from './isolated-plan.mjs';
import { requireIsolatedPlan } from './isolated-plan-contract.mjs';
import { createScaledPlans, validateScaledPlans } from './scaled-isolated-plan.mjs';
import { createVectorPlans, validateVectorPlans } from './vector-isolated-plan.mjs';
import { isolatedJobLabel, isolatedJobName } from '../../../site/Features/BenchmarkComparisons/isolated-contracts.mjs';

const representatives = Object.freeze({ Qdrant: 'VectorExact', RabbitMQ: 'QueueCycle', KurrentDB: 'StreamAppend' });
const databaseKeys = Object.freeze({ KeyLoad: 'keyload', 'PostgreSQL + pgvector': 'postgresql',
  Qdrant: 'qdrant', RabbitMQ: 'rabbitmq', Redis: 'redis', Neo4j: 'neo4j', MongoDB: 'mongodb',
  OpenSearch: 'opensearch', KurrentDB: 'kurrentdb', SurrealDB: 'surrealdb', HelixDB: 'helixdb' });

export function createPreflightMatrix(plan) {
  const canonical = validateIsolatedPlan(plan);
  const include = canonical.cells.filter(cell => cell.scenario === (representatives[cell.target] ?? 'PointRead'));
  requireIsolatedPlan(include.length === 33 && new Set(include.map(cell => cell.id)).size === 33);
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

export function createDatabaseMatrices(plan, scaledPlans = createScaledPlans(), vectorPlans = createVectorPlans()) {
  const canonical = validateIsolatedPlan(plan);
  const scales = validateScaledPlans(scaledPlans);
  const vectors = validateVectorPlans(vectorPlans);
  const checks = createPreflightMatrix(canonical).include;
  requireIsolatedPlan(new Set(canonical.cells.map(cell => cell.target)).size === Object.keys(databaseKeys).length);
  return Object.fromEntries(Object.entries(databaseKeys).map(([target, key]) => {
    const include = [...checks.filter(cell => cell.target === target).map(cell => databaseRow(cell, true)),
      ...canonical.cells.filter(cell => cell.target === target).map(cell => databaseRow(cell, false)),
      ...scales.flatMap(profile => profile.cells.filter(cell => cell.target === target).map(cell => databaseRow(cell, false))),
      ...vectors.flatMap(profile => profile.cells.filter(cell => cell.target === target).map(cell => databaseRow(cell, false)))];
    requireIsolatedPlan(include.length === 129 && new Set(include.map(cell => cell.jobName)).size === include.length
      && include.every(row => row.vectorProfile === null || row.scaleProfile === null));
    return [key, { include }];
  }));
}
