import path from 'node:path';
import { hashPattern, positive, shaPattern } from './isolated-github-contract.mjs';
import { readJson } from './isolated-github-files.mjs';
import { verifySiteFiles } from './site-isolated-github-files.mjs';
import { SITE_GH, exact, requireSite, safeRelative } from './site-isolated-github-contract.mjs';
import { sameSiteIdentity, validateSiteIsolatedReceipt } from './site-isolated-github-receipt.mjs';

function validateUnavailableReceipt(receipt) {
  requireSite(exact(receipt, ['schemaVersion', 'state', 'mode', 'publishEligible', 'source', 'producer', 'metadataFiles'])
    && receipt.schemaVersion === SITE_GH.version && receipt.state === 'unavailable' && receipt.mode === SITE_GH.publish
    && receipt.publishEligible === true && receipt.producer === null
    && exact(receipt.source, ['website', 'control']) && Object.values(receipt.source).every(value => shaPattern.test(value ?? ''))
    && Array.isArray(receipt.metadataFiles) && receipt.metadataFiles.length > 0);
  const paths = new Set();
  for (const file of receipt.metadataFiles) {
    requireSite(exact(file, SITE_GH.fileKeys) && safeRelative(file.path) && file.path.startsWith('metadata/')
      && !paths.has(file.path.toLowerCase()) && positive(file.bytes) && hashPattern.test(file.sha256 ?? ''));
    paths.add(file.path.toLowerCase());
  }
  return receipt;
}

export async function verifySiteIsolatedFreshness({ before, after }) {
  const previousRaw = await readJson(before, SITE_GH.jsonBytes);
  const currentRaw = await readJson(after, SITE_GH.jsonBytes);
  if (previousRaw.state === 'unavailable' || currentRaw.state === 'unavailable') {
    requireSite(previousRaw.state === 'unavailable' && currentRaw.state === 'unavailable');
    const previous = validateUnavailableReceipt(previousRaw);
    const current = validateUnavailableReceipt(currentRaw);
    requireSite(previous.state === 'unavailable' && current.state === 'unavailable'
      && sameSiteIdentity(previous.source, current.source));
    await verifySiteFiles(path.dirname(before), previous.metadataFiles);
    await verifySiteFiles(path.dirname(after), current.metadataFiles);
    return { fresh: true, state: 'unavailable', source: current.source, producer: null };
  }
  const previous = validateSiteIsolatedReceipt(previousRaw);
  const current = validateSiteIsolatedReceipt(currentRaw);
  requireSite(previous.state === SITE_GH.archiveState && current.state === SITE_GH.metadataState
    && previous.publishEligible && current.publishEligible && previous.mode === SITE_GH.publish && current.mode === SITE_GH.publish);
  for (const field of ['source', 'repository', 'workflow', 'run', 'cohort', 'aggregateJob', 'artifacts', 'workers', 'image']) {
    requireSite(sameSiteIdentity(previous[field], current[field]));
  }
  return { fresh: true, source: current.source, run: current.run, aggregateJobId: current.aggregateJob.id,
    suiteArtifactId: current.artifacts.suite.id, providerArtifactId: current.artifacts.provider.id };
}
