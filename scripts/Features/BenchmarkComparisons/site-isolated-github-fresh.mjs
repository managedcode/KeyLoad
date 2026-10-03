import { readJson } from './isolated-github-files.mjs';
import { SITE_GH, requireSite } from './site-isolated-github-contract.mjs';
import { sameSiteIdentity, validateSiteIsolatedReceipt } from './site-isolated-github-receipt.mjs';

export async function verifySiteIsolatedFreshness({ before, after }) {
  const previous = validateSiteIsolatedReceipt(await readJson(before, SITE_GH.jsonBytes));
  const current = validateSiteIsolatedReceipt(await readJson(after, SITE_GH.jsonBytes));
  requireSite(previous.state === SITE_GH.archiveState && current.state === SITE_GH.metadataState
    && previous.publishEligible && current.publishEligible && previous.mode === SITE_GH.publish && current.mode === SITE_GH.publish);
  for (const field of ['source', 'repository', 'workflow', 'run', 'cohort', 'aggregateJob', 'artifacts', 'workers', 'image']) {
    requireSite(sameSiteIdentity(previous[field], current[field]));
  }
  return { fresh: true, source: current.source, run: current.run, aggregateJobId: current.aggregateJob.id,
    suiteArtifactId: current.artifacts.suite.id, providerArtifactId: current.artifacts.provider.id };
}
