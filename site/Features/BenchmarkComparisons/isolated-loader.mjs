import { sha256 } from './measurement-loader.mjs';
import { ISOLATED, WIRE, assertIsolated, exact, isolatedCells, matches, same } from './isolated-contracts.mjs';
import { validateCatalog, validateCohort, validateOptions, validateWorkerMetadata } from './isolated-metadata.mjs';
import { retainCommonFacts, validateCompactReport } from './isolated-report-validation.mjs';
import { assertNotAborted, confinedUrl, fetchIsolatedBytes, parseIsolatedJson } from './isolated-http.mjs';

function validateSize(value, maximum) {
  assertIsolated(new TextEncoder().encode(JSON.stringify(value)).byteLength <= maximum);
}

export function validateIsolatedCatalog(value) {
  validateSize(value, ISOLATED.catalogBytes);
  return validateCatalog(value);
}

export function validateIsolatedProjection(value, catalog) {
  validateIsolatedCatalog(catalog);
  validateSize(value, ISOLATED.projectionBytes);
  assertIsolated(exact(value, WIRE.projection) && value.schemaVersion === ISOLATED.projectionVersion &&
    value.profile === ISOLATED.profile && matches(ISOLATED.hash, value.datasetSha256) &&
    Array.isArray(value.workers) && value.workers.length === ISOLATED.workers);
  validateCohort(value.cohort);
  assertIsolated(same(value.cohort, catalog.cohort, WIRE.cohort));
  validateOptions(value.options);
  const expected = new Map(isolatedCells().map(cell => [cell.id, cell]));
  const identities = { jobs: new Set(), artifacts: new Set() };
  const common = { engines: new Map(), topologies: new Map() };
  for (const worker of value.workers) {
    assertIsolated(worker !== null && typeof worker === 'object' && expected.has(worker.id));
    validateWorkerMetadata(worker, expected.get(worker.id), value.cohort, identities);
    expected.delete(worker.id);
    validateCompactReport(worker.report, worker, value.cohort, value.datasetSha256);
    retainCommonFacts(common, worker.report, worker.nodeCount);
  }
  assertIsolated(expected.size === 0);
  return value;
}

export async function loadIsolatedCatalog({ catalogUrl, signal }) {
  const page = globalThis.document?.baseURI ?? globalThis.location?.href;
  const url = confinedUrl(catalogUrl, page ? new URL('.', page).href : undefined);
  return validateIsolatedCatalog(parseIsolatedJson(await fetchIsolatedBytes(url, ISOLATED.catalogBytes, signal)));
}

export async function loadIsolatedProjection({ entry, baseUrl, signal }) {
  validateIsolatedCatalog(entry);
  const base = confinedUrl(baseUrl, undefined, true);
  const url = confinedUrl(entry.projection.path, base.href);
  const bytes = await fetchIsolatedBytes(url, ISOLATED.projectionBytes, signal);
  assertIsolated(await sha256(bytes) === entry.projection.sha256);
  assertNotAborted(signal);
  return validateIsolatedProjection(parseIsolatedJson(bytes), entry);
}
