import { validateIsolatedPlan } from './isolated-plan.mjs';
import { requireIsolatedPlan } from './isolated-plan-contract.mjs';
import { isolatedJobLabel, isolatedJobName } from '../../../site/Features/BenchmarkComparisons/isolated-contracts.mjs';

const representatives = Object.freeze({ Qdrant: 'VectorExact', RabbitMQ: 'QueueCycle', KurrentDB: 'StreamAppend' });
const databaseKeys = Object.freeze({ KeyLoad: 'keyload', 'PostgreSQL + pgvector': 'postgresql',
  Qdrant: 'qdrant', RabbitMQ: 'rabbitmq', Redis: 'redis', Neo4j: 'neo4j', MongoDB: 'mongodb',
  OpenSearch: 'opensearch', KurrentDB: 'kurrentdb' });

export function createPreflightMatrix(plan) {
  const canonical = validateIsolatedPlan(plan);
  const include = canonical.cells.filter(cell => cell.scenario === (representatives[cell.target] ?? 'PointRead'));
  requireIsolatedPlan(include.length === 27 && new Set(include.map(cell => cell.id)).size === 27);
  return { include };
}

function databaseRow(cell, preflight) {
  return { ...cell, preflight, label: isolatedJobLabel(cell, preflight), jobName: isolatedJobName(cell, preflight),
    artifactPrefix: preflight ? 'comparison-preflight-' : 'comparison-worker-',
    qualificationPrefix: preflight ? 'comparison-preflight-qualification-' : 'comparison-case-qualification-' };
}

export function createDatabaseMatrices(plan) {
  const canonical = validateIsolatedPlan(plan);
  const checks = createPreflightMatrix(canonical).include;
  requireIsolatedPlan(new Set(canonical.cells.map(cell => cell.target)).size === Object.keys(databaseKeys).length);
  return Object.fromEntries(Object.entries(databaseKeys).map(([target, key]) => {
    const include = [...checks.filter(cell => cell.target === target).map(cell => databaseRow(cell, true)),
      ...canonical.cells.filter(cell => cell.target === target).map(cell => databaseRow(cell, false))];
    requireIsolatedPlan(include.length === 33 && new Set(include.map(cell => cell.jobName)).size === include.length);
    return [key, { include }];
  }));
}
