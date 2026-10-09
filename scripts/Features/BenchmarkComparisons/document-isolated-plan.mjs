import { readFileSync } from 'node:fs';
import { isDeepStrictEqual } from 'node:util';
import { readIsolatedContract, requireIsolatedPlan } from './isolated-plan-contract.mjs';

const source = new URL('../../../benchmarks/KeyLoad.Comparisons/Features/BenchmarkComparisons/Documents/document-contract.json', import.meta.url);
const canonical = JSON.parse(readFileSync(source, 'utf8'));
const scenarios = ['SequentialRead', 'RandomRead', 'Create', 'Update', 'Delete', 'ReadUpdate50', 'ReadUpdate95', 'MixedCrud'];
requireIsolatedPlan(canonical.schemaVersion === 1 && canonical.family === 'document-v1'
  && isDeepStrictEqual(canonical.nodeCounts, [1, 3]) && isDeepStrictEqual(canonical.datasetSizes, [100_000, 1_000_000])
  && isDeepStrictEqual(canonical.scenarios, scenarios) && canonical.operations === 100_000 && canonical.repetitions === 3
  && canonical.clients === 16 && canonical.ingestion.scenario === 'Ingest' && canonical.ingestion.records === 1_000_000
  && isDeepStrictEqual(canonical.ingestion.clients, [1, 10, 500]) && canonical.maximumOutstandingPerClient === 1);

export const DOCUMENT = Object.freeze({ family: 'document-v1', kind: 'document-worker.v1', schemaVersion: 1,
  raw: 'document-worker.json', artifactPrefix: 'comparison-document-worker-', cells: 418, perTarget: 38 });
export const readDocumentContract = () => structuredClone(canonical);
export const documentScenarioSlug = scenario => scenario.replace(/([a-z0-9])([A-Z])/gu, '$1-$2').toLowerCase();
export function documentProfile(scenario, records, clients) {
  const ordinary = canonical.scenarios.includes(scenario) && canonical.datasetSizes.includes(records) && clients === canonical.clients;
  const ingestion = scenario === canonical.ingestion.scenario && records === canonical.ingestion.records && canonical.ingestion.clients.includes(clients);
  requireIsolatedPlan(ordinary || ingestion);
  return `documents-${records === 100_000 ? '100k' : '1m'}-${documentScenarioSlug(scenario)}-c${clients}`;
}
export function createDocumentPlan(contract = readDocumentContract(), native = readIsolatedContract()) {
  requireIsolatedPlan(isDeepStrictEqual(contract, canonical) && isDeepStrictEqual(native, readIsolatedContract()));
  const workloads = [...contract.datasetSizes.flatMap(records => contract.scenarios.map(scenario => ({ scenario, records, clients: contract.clients }))),
    ...contract.ingestion.clients.map(clients => ({ scenario: contract.ingestion.scenario, records: contract.ingestion.records, clients }))];
  const cells = native.targets.flatMap(target => contract.nodeCounts.flatMap(nodeCount => workloads.map(workload => {
    const profile = documentProfile(workload.scenario, workload.records, workload.clients);
    const targetId = target.toLowerCase().replace(/[^a-z0-9]+/gu, '-').replace(/^-|-$/gu, '');
    return { id: `${targetId}-n${nodeCount}-${profile}`, target, nodeCount, scenario: 'PointRead', profile,
      family: DOCUMENT.family, documentScenario: workload.scenario, documentRecords: workload.records, documentClients: workload.clients };
  })));
  requireIsolatedPlan(cells.length === DOCUMENT.cells && new Set(cells.map(cell => cell.id)).size === cells.length);
  return { schemaVersion: 1, family: DOCUMENT.family, contract: structuredClone(contract), cells };
}
export function validateDocumentPlan(plan) {
  requireIsolatedPlan(isDeepStrictEqual(plan, createDocumentPlan()));
  return structuredClone(plan);
}
export function documentJobName(cell) {
  return `${cell.target} / ${cell.nodeCount} node${cell.nodeCount === 1 ? '' : 's'} / Documents / ${cell.documentScenario} / ${cell.documentRecords} records / ${cell.documentClients} clients`;
}
export function documentMatrixRow(cell) {
  return { ...cell, preflight: false, label: documentJobName(cell).slice(cell.target.length + 3), jobName: documentJobName(cell),
    scaleProfile: null, vectorProfile: null, artifactPrefix: DOCUMENT.artifactPrefix,
    qualificationPrefix: 'comparison-document-qualification-' };
}
export function selectedDocumentCell(environment) {
  const cell = createDocumentPlan().cells.find(item => item.id === environment.KEYLOAD_COMPARISON_CELL_ID);
  requireIsolatedPlan(cell !== undefined && environment.KEYLOAD_MATRIX_KIND === 'documents'
    && cell.target === environment.Benchmarks__Target && String(cell.nodeCount) === environment.Benchmarks__NodeCount
    && environment.Benchmarks__Scenario === cell.scenario && environment.Benchmarks__EvidenceProfile === cell.profile
    && environment.Benchmarks__DocumentScenario === cell.documentScenario
    && environment.Benchmarks__DocumentRecords === String(cell.documentRecords)
    && environment.Benchmarks__DocumentClients === String(cell.documentClients)
    && documentJobName(cell) === environment.KEYLOAD_COMPARISON_JOB_NAME
    && ['KEYLOAD_SCALE_PROFILE', 'KEYLOAD_VECTOR_PROFILE', 'KEYLOAD_OPEN_LOOP_RATE', 'KEYLOAD_OPEN_LOOP_CANCELLATION_PROOF', 'Benchmarks__VectorProfile']
      .every(key => (environment[key] ?? '') === ''));
  return cell;
}
