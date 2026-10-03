import path from 'node:path';
import { absolutePath, existingPath } from './aggregate-files.mjs';
import { createDirectory } from './image-bundle-files.mjs';
import { GH } from './isolated-github-contract.mjs';
import { captureApi, downloadArtifact } from './isolated-github-api.mjs';
import { initializeTransport } from './isolated-github-transport.mjs';
import { validateDownloadedArchive } from './isolated-github-stream.mjs';
import { writeJson } from './isolated-github-files.mjs';
import { SITE_GH, requireSite } from './site-isolated-github-contract.mjs';
import { createSiteIsolatedContext, parseSiteCaptureArguments } from './site-isolated-github-context.mjs';
import { captureSitePages } from './site-isolated-github-api.mjs';
import { selectSiteIsolatedEvidence } from './site-isolated-github-runs.mjs';
import { proveSiteIsolatedEvidence } from './site-isolated-github-proof.mjs';
import { siteMetadataFiles } from './site-isolated-github-files.mjs';

async function captureSelection(input, context) {
  const directory = path.join(input, SITE_GH.metadata);
  await captureApi(`${GH.api}/workflows/ci.yml`, path.join(directory, 'workflow.json'), false, context);
  await captureSitePages(`${GH.api}/workflows/ci.yml/runs?branch=main&event=push`, directory, 'workflow_runs', context);
  const attempts = await createDirectory(path.join(directory, 'attempts'));
  for (let pair = 0; pair <= SITE_GH.pairs; pair += 1) {
    const selected = await selectSiteIsolatedEvidence({ input, mode: context.mode, requestedRun: context.requestedRun });
    if (selected.state === 'selected') {
      await captureSitePages(`${GH.api}/runs/${selected.run.id}/artifacts`, directory, 'artifacts', context);
      return selected;
    }
    const runRoot = path.join(attempts, String(selected.runId));
    try { await existingPath(runRoot, true); } catch (error) {
      if (error.code !== 'ENOENT') throw error;
      await createDirectory(runRoot);
    }
    const root = await createDirectory(path.join(runRoot, String(selected.attempt)));
    const route = `${GH.api}/runs/${selected.runId}/attempts/${selected.attempt}`;
    await captureApi(route, path.join(root, 'run-attempt.json'), false, context);
    await captureSitePages(`${route}/jobs`, root, 'jobs', context);
  }
  throw new Error(SITE_GH.failure);
}

async function capture({ environment = process.env, args = process.argv.slice(3) }, download) {
  const parsed = parseSiteCaptureArguments(args);
  const input = absolutePath(parsed.input);
  const context = createSiteIsolatedContext(environment, parsed);
  await existingPath(path.dirname(input), true);
  await createDirectory(input);
  const directory = await createDirectory(path.join(input, SITE_GH.metadata));
  await initializeTransport(context, input, directory);
  const selected = await captureSelection(input, context);
  const receipt = await proveSiteIsolatedEvidence({ input, selection: selected, source: context.source, mode: context.mode });
  if (download) {
    await createDirectory(path.join(input, SITE_GH.archives));
    for (const name of ['suite', 'provider']) {
      const artifact = receipt.artifacts[name];
      const native = { ...artifact, size_in_bytes: artifact.sizeInBytes };
      const archive = path.join(input, SITE_GH.archives, artifact.name + '.zip');
      const limit = name === 'suite' ? SITE_GH.suiteBytes : SITE_GH.providerBytes;
      await downloadArtifact(native, archive, limit, context);
      await validateDownloadedArchive(archive, native, limit);
    }
    receipt.metadataFiles = await siteMetadataFiles(input);
  }
  requireSite(Buffer.byteLength(`${JSON.stringify(receipt, null, 2)}\n`) <= SITE_GH.jsonBytes);
  await writeJson(path.join(input, SITE_GH.metadataProof), receipt);
  return receipt;
}

export const captureSiteIsolatedEvidence = options => capture(options ?? {}, true);
export const captureSiteIsolatedMetadata = options => capture(options ?? {}, false);
