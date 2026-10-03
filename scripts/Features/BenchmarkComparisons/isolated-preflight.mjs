import { validateIsolatedPlan } from './isolated-plan.mjs';
import { requireIsolatedPlan } from './isolated-plan-contract.mjs';

const representatives = Object.freeze({ Qdrant: 'VectorExact', RabbitMQ: 'QueueCycle', KurrentDB: 'StreamAppend' });

export function createPreflightMatrix(plan) {
  const canonical = validateIsolatedPlan(plan);
  const include = canonical.cells.filter(cell => cell.scenario === (representatives[cell.target] ?? 'PointRead'));
  requireIsolatedPlan(include.length === 27 && new Set(include.map(cell => cell.id)).size === 27);
  return { include };
}
