import { pathToFileURL } from 'node:url';
import { isDeepStrictEqual } from 'node:util';
import { isolatedPlanLimits, readIsolatedContract, requireCanonicalContract, requireIsolatedPlan } from './isolated-plan-contract.mjs';
import { runIsolatedPlanCli } from './isolated-plan-cli.mjs';

export { readIsolatedContract };

function slug(value) {
  return value.toLowerCase().replace(/[^a-z0-9]+/gu, '-').replace(/^-|-$/gu, '');
}

function cell(target, nodeCount, scenario, profile, family) {
  const scenarioId = slug(scenario.replace(/([a-z0-9])([A-Z])/gu, '$1-$2'));
  const id = slug(target) + '-n' + nodeCount + '-' + scenarioId;
  requireIsolatedPlan(id.length <= isolatedPlanLimits.idLength && /^[a-z0-9]+(?:-[a-z0-9]+)*$/u.test(id));
  return { id, target, nodeCount, scenario, profile, family };
}

function familyCells(contract, family, scenarios) {
  return contract.targets.flatMap(target => contract.nodeCounts.flatMap(nodeCount =>
    scenarios.map(scenario => cell(target, nodeCount, scenario, contract.profile, family))));
}

export function createIsolatedPlan(contract = readIsolatedContract()) {
  requireCanonicalContract(contract);
  const crud = familyCells(contract, 'crud', contract.crudScenarios);
  const specialized = familyCells(contract, 'specialized', contract.specializedScenarios);
  const cells = [...crud, ...specialized];
  requireIsolatedPlan(crud.length === isolatedPlanLimits.crud && specialized.length === isolatedPlanLimits.specialized
    && cells.length === isolatedPlanLimits.cells && new Set(cells.map(value => value.id)).size === cells.length);
  requireIsolatedPlan(crud.length < isolatedPlanLimits.matrix && specialized.length < isolatedPlanLimits.matrix);
  return {
    schemaVersion: contract.schemaVersion,
    workerSchemaVersion: contract.workerSchemaVersion,
    profile: contract.profile,
    options: structuredClone(contract.options),
    cells: structuredClone(cells),
    matrices: { crud: { include: crud }, specialized: { include: specialized } },
  };
}

export function validateIsolatedPlan(plan, contract = readIsolatedContract()) {
  const expected = createIsolatedPlan(contract);
  requireIsolatedPlan(isDeepStrictEqual(plan, expected));
  return structuredClone(plan);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    await runIsolatedPlanCli(process.argv.slice(2), createIsolatedPlan());
  } catch {
    process.stderr.write('Isolated comparison planning failed.\n');
    process.exitCode = 1;
  }
}
