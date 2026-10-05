import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { absolutePath, existingPath } from './aggregate-files.mjs';
import { validateIsolatedPlan } from './isolated-plan.mjs';
import { verifySourceCheckout } from './prepare-images.mjs';
import { createDirectory } from './image-bundle-files.mjs';
import { GH } from './isolated-github-contract.mjs';
import { contextForProfile, createGitHubContext, parseCollectArguments } from './isolated-github-context.mjs';
import { captureRun } from './isolated-github-api.mjs';
import { readJson, writeJson } from './isolated-github-files.mjs';
import { requireCompleteProof, selectCompletedEvidence } from './isolated-github-selection.mjs';
import { collectImageEvidence } from './isolated-github-images.mjs';
import { collectWorkerEvidence } from './isolated-github-workers.mjs';
import { initializeTransport } from './isolated-github-transport.mjs';
import { createCompositePlan, validateScaledPlans } from './scaled-isolated-plan.mjs';

async function prepareCapture(input, scaledPlans) {
  await createDirectory(input);
  await createDirectory(path.join(input, 'data'));
  await createDirectory(path.join(input, 'data', 'workers'));
  await createDirectory(path.join(input, 'github'));
  await createDirectory(path.join(input, 'archives'));
  await createDirectory(path.join(input, 'github', 'plans'));
  await createDirectory(path.join(input, 'data', 'scaled'));
  await createDirectory(path.join(input, 'github', 'scaled'));
  for (const profile of scaledPlans) {
    const profileData = path.join(input, 'data', 'scaled', profile.profile);
    const profileProof = path.join(input, 'github', 'scaled', profile.profile);
    await createDirectory(profileData);
    await createDirectory(path.join(profileData, 'workers'));
    await createDirectory(profileProof);
  }
}

export async function collectGitHubEvidence(environment = process.env, argv = process.argv.slice(2)) {
  const paths = parseCollectArguments(argv);
  const input = absolutePath(paths.input);
  await existingPath(paths.plan, false);
  await existingPath(paths.scalePlan, false);
  const context = createGitHubContext(environment, process.platform);
  const plan = validateIsolatedPlan(await readJson(paths.plan));
  const scalePlans = validateScaledPlans(await readJson(paths.scalePlan));
  await verifySourceCheckout(context.native);
  await prepareCapture(input, scalePlans);
  try {
    await initializeTransport(context, input, path.join(input, 'github'));
    const capture = await captureRun(path.join(input, 'github'), context, true);
    const selected = selectCompletedEvidence(capture, context, plan, scalePlans);
    const image = await collectImageEvidence(input, selected.image, context);
    await writeJson(path.join(input, 'github', 'image-proof.json'), image);
    const profiles = [plan, ...scalePlans];
    const compositePlan = createCompositePlan(plan, scalePlans);
    await writeJson(path.join(input, 'github', 'composite-plan.json'), compositePlan);
    await writeJson(path.join(input, 'github', 'plans', `${plan.profile}.json`), plan);
    for (const profile of scalePlans) {
      await writeJson(path.join(input, 'github', 'plans', `${profile.profile}.json`), profile);
    }
    for (const profilePlan of profiles) {
      const profileContext = contextForProfile(context, profilePlan.profile);
      const dataRoot = profilePlan.profile === plan.profile ? path.join(input, 'data')
        : path.join(input, 'data', 'scaled', profilePlan.profile);
      const cells = [];
      for (const item of selected.cells.filter(value => value.cell.profile === profilePlan.profile)) {
        cells.push(await collectWorkerEvidence(input, dataRoot, item, image.images, profileContext, profilePlan));
      }
      const proof = requireCompleteProof({ schemaVersion: 1, cohort: profileContext.cohort, cells }, profilePlan);
      const proofPath = profilePlan.profile === plan.profile ? path.join(input, 'github', 'proof.json')
        : path.join(input, 'github', 'scaled', profilePlan.profile, 'proof.json');
      await writeJson(proofPath, proof);
    }
    return { schemaVersion: 2, profiles: profiles.map(item => item.profile) };
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
