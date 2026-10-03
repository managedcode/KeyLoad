import { constants } from 'node:fs';
import { open } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { validateEntryArguments } from './image-inputs.mjs';
import { verifySourceCheckout } from './prepare-images.mjs';
import { createDirectory, requireOutputFile } from './image-bundle-files.mjs';
import { createGitHubContext, requireCurrentJobName } from './isolated-github-context.mjs';
import { GH, requireGitHub } from './isolated-github-contract.mjs';
import { captureCurrentJobPages } from './isolated-github-api.mjs';
import { captureFreshCurrentJob } from './isolated-current-job.mjs';
import { validateJobIdentity } from './isolated-github-validation.mjs';
import { writeJson } from './isolated-github-files.mjs';
import { initializeTransport } from './isolated-github-transport.mjs';

async function appendJobEnvironment(target, id) {
  await requireOutputFile(target);
  const handle = await open(target, constants.O_WRONLY | constants.O_APPEND | (constants.O_NOFOLLOW ?? 0));
  try {
    await handle.writeFile(`KEYLOAD_COMPARISON_JOB_ID=${id}\n`);
    await handle.sync();
  } finally { await handle.close(); }
}

export async function captureCurrentJob(environment = process.env, argv = process.argv.slice(2)) {
  validateEntryArguments(argv);
  const context = createGitHubContext(environment, process.platform);
  const name = requireCurrentJobName(environment.KEYLOAD_COMPARISON_JOB_NAME, context.plan);
  requireGitHub(environment.GITHUB_WORKFLOW_REF === `${GH.repository}/${GH.workflowPath}@refs/heads/main`);
  requireGitHub(typeof environment.GITHUB_ENV === 'string' && path.isAbsolute(environment.GITHUB_ENV));
  await requireOutputFile(environment.GITHUB_ENV);
  await verifySourceCheckout(context.native);
  const directory = await createDirectory(path.join(context.native.runnerTemp, GH.captureDirectory));
  await initializeTransport(context, directory, directory);
  const discovered = validateJobIdentity(await captureCurrentJobPages(directory, context, name), context.cohort, name);
  const job = await captureFreshCurrentJob(discovered, directory, context, name);
  await writeJson(path.join(directory, 'job.json'), job);
  await writeJson(path.join(directory, 'cohort.json'), context.cohort);
  await appendJobEnvironment(environment.GITHUB_ENV, job.id);
  return job;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try { await captureCurrentJob(); } catch {
    process.stderr.write(`${GH.failure}\n`);
    process.exitCode = 1;
  }
}
