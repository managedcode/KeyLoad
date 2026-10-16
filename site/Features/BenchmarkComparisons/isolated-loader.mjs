import { sha256 } from './measurement-loader.mjs';
import { assertNotAborted, confinedUrl, fetchIsolatedBytes, parseIsolatedJson } from './isolated-http.mjs';
import { SESSION, exactSession, requireSession, sessionHash, validateSessionCohort } from '../../../scripts/Features/BenchmarkComparisons/session-contract.mjs';
import { validateSessionAggregate } from '../../../scripts/Features/BenchmarkComparisons/session-aggregate-contract.mjs';

export function validateIsolatedCatalog(value) {
  requireSession(exactSession(value, ['schemaVersion', 'generatedAt', 'siteSourceRevision', 'measuredSourceRevision',
    'evidenceUrl', 'cohort', 'aggregate', 'projection', 'rawLocation']) && value.schemaVersion === SESSION.schema
    && /^\d{4}-\d\d-\d\dT/.test(value.generatedAt ?? '') && Number.isFinite(Date.parse(value.generatedAt))
    && /^[a-f0-9]{40}$/.test(value.siteSourceRevision ?? '') && value.measuredSourceRevision === value.cohort?.sourceRevision
    && value.evidenceUrl === `https://github.com/${SESSION.repository}/actions/runs/${value.cohort?.runId}`
    && value.rawLocation === 'githubActionsArtifacts');
  validateSessionCohort(value.cohort);
  for (const [key, location] of [['aggregate', 'isolated/aggregate.json'], ['projection', 'isolated/projection.json']])
    requireSession(exactSession(value[key], ['path', 'sha256']) && value[key].path === location && sessionHash(value[key].sha256));
  return value;
}
export function validateIsolatedProjection(value, catalog) {
  validateIsolatedCatalog(catalog);
  requireSession(exactSession(value, ['schemaVersion', 'cohort', 'plan', 'images', 'preparation', 'workers',
    'profile', 'options', 'datasetSha256']) && value.profile === SESSION.profile && value.datasetSha256 === null
    && exactSession(value.options, ['operations', 'concurrency', 'payloadBytes']) && value.options.operations === SESSION.operations
    && value.options.concurrency === SESSION.concurrency && value.options.payloadBytes === 'not recorded');
  const { profile, options, datasetSha256, ...aggregate } = value;
  validateSessionAggregate(aggregate);
  requireSession(JSON.stringify(value.cohort) === JSON.stringify(catalog.cohort));
  return value;
}
export async function loadIsolatedCatalog({ catalogUrl, signal }) {
  const page = globalThis.document?.baseURI ?? globalThis.location?.href;
  const url = confinedUrl(catalogUrl, page ? new URL('.', page).href : undefined);
  return validateIsolatedCatalog(parseIsolatedJson(await fetchIsolatedBytes(url, 65_536, signal)));
}
export async function loadIsolatedProjection({ entry, baseUrl, signal }) {
  validateIsolatedCatalog(entry);
  const base = confinedUrl(baseUrl, undefined, true);
  const url = confinedUrl(entry.projection.path, base.href);
  const bytes = await fetchIsolatedBytes(url, SESSION.aggregateBytes, signal);
  requireSession(await sha256(bytes) === entry.projection.sha256);
  assertNotAborted(signal);
  return validateIsolatedProjection(parseIsolatedJson(bytes), entry);
}
