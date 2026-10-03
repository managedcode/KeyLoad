import { readdir } from 'node:fs/promises';
import { join } from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { AGGREGATE, SUPPORT as NATIVE_SUPPORT } from '../../../scripts/Features/BenchmarkComparisons/aggregate-contracts.mjs';
import { createIsolatedPlan, readIsolatedContract } from '../../../scripts/Features/BenchmarkComparisons/isolated-plan.mjs';
import { validateAggregateProof, validateWorkerEnvelope } from '../../../scripts/Features/BenchmarkComparisons/aggregate-validation.mjs';
import { existingPath, hashBytes, parseBytes, rawFile, readBytes } from '../../../scripts/Features/BenchmarkComparisons/aggregate-files.mjs';
import { ISOLATED, SUPPORT, WIRE, assertIsolated, exact, isolatedCells, matches } from './isolated-contracts.mjs';
import { validateCohort, validateOptions, validateWorkerMetadata } from './isolated-metadata.mjs';
import { retainCommonFacts } from './isolated-report-validation.mjs';

function verifyBrowserContract(contract, plan) {
  for (const [browser, native] of [['targets', 'targets'], ['nodes', 'nodeCounts'], ['crud', 'crudScenarios'],
    ['specialized', 'specializedScenarios'], ['options', 'options'], ['unsupportedTopologies', 'unsupportedTopologies']]) {
    assertIsolated(isDeepStrictEqual(ISOLATED[browser], contract[native]));
  }
  assertIsolated(ISOLATED.profile === contract.profile &&
    isDeepStrictEqual(Object.keys(SUPPORT).sort(), Object.keys(NATIVE_SUPPORT).sort()));
  assertIsolated(['failed', 'failure', 'success', 'failureReason'].every(key => ISOLATED[key] === AGGREGATE[key]));
  for (const target of ISOLATED.targets) {
    assertIsolated(isDeepStrictEqual([...SUPPORT[target]].sort(), [...NATIVE_SUPPORT[target]].sort()));
  }
  const browserIds = isolatedCells().map(cell => cell.id).sort();
  assertIsolated(isDeepStrictEqual(browserIds, plan.cells.map(cell => cell.id).sort()));
}

function validateManifest(value, plan) {
  assertIsolated(exact(value, WIRE.projection) && value.schemaVersion === ISOLATED.aggregateVersion &&
    value.profile === ISOLATED.profile && (value.datasetSha256 === null || matches(ISOLATED.hash, value.datasetSha256)) &&
    Array.isArray(value.workers) && value.workers.length === plan.cells.length);
  validateCohort(value.cohort);
  validateOptions(value.options);
  const expected = new Map(plan.cells.map(cell => [cell.id, cell]));
  const identities = { jobs: new Set(), artifacts: new Set() };
  for (const worker of value.workers) {
    assertIsolated(expected.has(worker.id));
    validateWorkerMetadata(worker, expected.get(worker.id), value.cohort, identities, WIRE.metadata);
    expected.delete(worker.id);
  }
  return value;
}

async function validateInputInventory(input, cells) {
  await existingPath(input, true);
  const roots = await readdir(input);
  assertIsolated(roots.length === 2 && roots.includes(AGGREGATE.manifest) && roots.includes(AGGREGATE.workers));
  const workers = join(input, AGGREGATE.workers);
  await existingPath(workers, true);
  const expected = new Set(cells.map(cell => cell.id));
  const entries = await readdir(workers, { withFileTypes: true });
  assertIsolated(entries.length === expected.size && entries.every(entry => expected.has(entry.name) &&
    entry.isDirectory() && !entry.isSymbolicLink()));
  for (const cell of cells) {
    const path = join(workers, cell.id);
    await existingPath(path, true);
    const files = await readdir(path);
    assertIsolated(files.length === 1 && files[0] === AGGREGATE.raw);
  }
}

function compactReport(report) {
  if (report === null) return null;
  return { ...report, cases: report.cases.map(({ samples, ...item }) => item) };
}

async function projectWorker(input, metadata, cell, manifest, contract, common) {
  const bytes = await readBytes(rawFile(input, cell.id), AGGREGATE.workerBytes);
  assertIsolated(hashBytes(bytes) === metadata.rawSha256);
  common.bytes += bytes.length;
  assertIsolated(common.bytes <= AGGREGATE.totalBytes);
  const envelope = validateWorkerEnvelope(parseBytes(bytes), cell, manifest.cohort, contract);
  assertIsolated(envelope.worker.jobId === metadata.job.id && envelope.disposition === metadata.disposition &&
    envelope.reason === metadata.reason && (envelope.report === null || envelope.report.datasetSha256 === manifest.datasetSha256));
  retainCommonFacts(common, envelope.report, cell.nodeCount);
  return { ...metadata, report: compactReport(envelope.report) };
}

// Authentication belongs to the workflow; this rechecks its unchanged aggregate and raw contract inputs.
export async function produceIsolatedProjection({ input }) {
  const contract = readIsolatedContract();
  const plan = createIsolatedPlan(contract);
  verifyBrowserContract(contract, plan);
  await validateInputInventory(input, plan.cells);
  const manifest = validateManifest(parseBytes(await readBytes(join(input, AGGREGATE.manifest), AGGREGATE.metadataBytes)), plan);
  validateAggregateProof({ schemaVersion: AGGREGATE.proofVersion, cohort: manifest.cohort,
    cells: manifest.workers.map(worker => ({ id: worker.id, job: worker.job, artifact: worker.artifact,
      workerSha256: worker.rawSha256 })) }, plan);
  const expected = new Map(plan.cells.map(cell => [cell.id, cell]));
  const common = { bytes: 0, engines: new Map(), topologies: new Map() };
  const workers = [];
  for (const worker of manifest.workers) {
    workers.push(await projectWorker(input, worker, expected.get(worker.id), manifest, contract, common));
  }
  assertIsolated(manifest.datasetSha256 === (common.report?.datasetSha256 ?? null));
  const projection = { ...manifest, schemaVersion: ISOLATED.projectionVersion, workers };
  assertIsolated(Buffer.byteLength(JSON.stringify(projection)) <= ISOLATED.projectionBytes);
  return projection;
}
