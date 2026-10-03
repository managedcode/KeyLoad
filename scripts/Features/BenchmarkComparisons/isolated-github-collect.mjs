import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { absolutePath, existingPath } from './aggregate-files.mjs';
import { validateIsolatedPlan } from './isolated-plan.mjs';
import { verifySourceCheckout } from './prepare-images.mjs';
import { createDirectory } from './image-bundle-files.mjs';
import { GH } from './isolated-github-contract.mjs';
import { createGitHubContext, parseCollectArguments } from './isolated-github-context.mjs';
import { captureRun } from './isolated-github-api.mjs';
import { readJson, writeJson } from './isolated-github-files.mjs';
import { requireCompleteProof, selectCompletedEvidence } from './isolated-github-selection.mjs';
import { collectImageEvidence } from './isolated-github-images.mjs';
import { collectWorkerEvidence } from './isolated-github-workers.mjs';
import { initializeTransport } from './isolated-github-transport.mjs';

async function prepareCapture(input) {
  await createDirectory(input);
  await createDirectory(path.join(input, 'data'));
  await createDirectory(path.join(input, 'data', 'workers'));
  await createDirectory(path.join(input, 'github'));
  await createDirectory(path.join(input, 'archives'));
}

export async function collectGitHubEvidence(environment = process.env, argv = process.argv.slice(2)) {
  const paths = parseCollectArguments(argv);
  const input = absolutePath(paths.input);
  await existingPath(paths.plan, false);
  const context = createGitHubContext(environment, process.platform);
  const plan = validateIsolatedPlan(await readJson(paths.plan));
  await verifySourceCheckout(context.native);
  await prepareCapture(input);
  try {
    await initializeTransport(context, input, path.join(input, 'github'));
    const capture = await captureRun(path.join(input, 'github'), context, true);
    const selected = selectCompletedEvidence(capture, context, plan);
    const image = await collectImageEvidence(input, selected.image, context);
    const cells = [];
    for (const item of selected.cells) cells.push(await collectWorkerEvidence(input, item, image.images, context));
    const proof = requireCompleteProof({ schemaVersion: 1, cohort: context.cohort, cells }, plan);
    await writeJson(path.join(input, 'github', 'image-proof.json'), image);
    await writeJson(path.join(input, 'github', 'proof.json'), proof);
    return proof;
  } catch {
    await writeJson(path.join(input, 'github', 'capture-failed.json'), { schemaVersion: 1, reason: GH.failure });
    throw new Error(GH.failure);
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { await collectGitHubEvidence(); } catch {
    process.stderr.write(`${GH.failure}\n`);
    process.exitCode = 1;
  }
}
