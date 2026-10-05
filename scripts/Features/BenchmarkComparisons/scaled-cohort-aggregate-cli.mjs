import path from 'node:path';
import { cp } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { AGGREGATE, requireValue } from './aggregate-contracts.mjs';
import { absolutePath } from './aggregate-files.mjs';
import { aggregateEvidence } from './aggregate-evidence.mjs';
import { hashRegularFile, readJson, writeJson } from './isolated-github-files.mjs';
import { createDirectory } from './image-bundle-files.mjs';
import { readIsolatedContract, validateIsolatedPlan } from './isolated-plan.mjs';
import { validateAggregateProof } from './aggregate-proof.mjs';
import { createScaleCohortReceipt } from './scaled-cohort-receipt.mjs';
import { validateCompositePlan, validateScaledPlans } from './scaled-isolated-plan.mjs';
import { validateVectorPlans } from './vector-isolated-plan.mjs';

const ARGUMENTS = Object.freeze(['input', 'plan', 'scale-plan', 'vector-plan', 'composite-plan', 'proof-root', 'output', 'scale-output']);

function parseArguments(arguments_) {
  const result = {};
  for (const argument of arguments_) {
    const matched = /^--([a-z-]+)=(.+)$/u.exec(argument);
    requireValue(matched !== null && ARGUMENTS.includes(matched[1]) && !Object.hasOwn(result, matched[1]), AGGREGATE.errors.input);
    result[matched[1]] = absolutePath(matched[2]);
  }
  requireValue(ARGUMENTS.every(key => Object.hasOwn(result, key)), AGGREGATE.errors.input);
  return result;
}

function requireSameCohort(controlProof, scaleProofs) {
  const control = controlProof.cohort;
  for (const proof of scaleProofs) {
    for (const field of ['sourceRevision', 'runId', 'attempt', 'repository', 'ref', 'workflow']) {
      requireValue(proof.cohort[field] === control[field], AGGREGATE.errors.cohort);
    }
  }
}

async function readProofs(root, control, scales, vectors) {
  const controlProof = await readJson(path.join(root, 'proof.json'), AGGREGATE.metadataBytes);
  const scaleProofs = [];
  for (const plan of scales) {
    scaleProofs.push(await readJson(path.join(root, 'scaled', plan.profile, 'proof.json'), AGGREGATE.metadataBytes));
  }
  const vectorProofs = [];
  for (const plan of vectors) {
    vectorProofs.push(await readJson(path.join(root, 'vector', plan.profile, 'proof.json'), AGGREGATE.metadataBytes));
  }
  const plans = [control, ...scales, ...vectors];
  const proofs = [controlProof, ...scaleProofs, ...vectorProofs];
  for (let index = 0; index < plans.length; index += 1) {
    requireValue(proofs[index].cohort.profile === plans[index].profile, AGGREGATE.errors.proof);
    validateAggregateProof(proofs[index], plans[index]);
  }
  requireSameCohort(controlProof, [...scaleProofs, ...vectorProofs]);
  return { controlProof, scaleProofs, vectorProofs };
}

async function publishScaleProfiles(input, proofRoot, output, scales, scaleProofs) {
  const manifests = [];
  for (let index = 0; index < scales.length; index += 1) {
    const profile = scales[index];
    const profileOutput = path.join(output, profile.profile);
    manifests.push(await aggregateEvidence({ input: path.join(input, 'data', 'scaled', profile.profile),
      output: profileOutput, plan: path.join(proofRoot, 'plans', `${profile.profile}.json`),
      proof: path.join(proofRoot, 'scaled', profile.profile, 'proof.json') }));
  }
  return manifests;
}

async function publishVectorProfiles(input, proofRoot, output, vectors, vectorProofs) {
  const root = await createDirectory(path.join(output, 'vector'));
  const manifests = [];
  for (let index = 0; index < vectors.length; index += 1) {
    const plan = vectors[index];
    manifests.push(await aggregateEvidence({ input: path.join(input, 'data', 'vector', plan.profile),
      output: path.join(root, plan.profile), plan: path.join(proofRoot, 'plans', `${plan.profile}.json`),
      proof: path.join(proofRoot, 'vector', plan.profile, 'proof.json') }));
  }
  return manifests;
}

async function aggregateScaleCohortCore(paths, scaleOutput) {
  const contract = readIsolatedContract();
  const control = validateIsolatedPlan(await readJson(paths.plan, AGGREGATE.metadataBytes), contract);
  const scales = validateScaledPlans(await readJson(paths['scale-plan'], AGGREGATE.metadataBytes), contract);
  const vectors = validateVectorPlans(await readJson(paths['vector-plan'], AGGREGATE.metadataBytes), contract);
  const composite = validateCompositePlan(await readJson(paths['composite-plan'], AGGREGATE.metadataBytes), contract);
  requireValue(composite.control.profile === control.profile &&
    JSON.stringify(composite.scaledProfiles) === JSON.stringify(scales) &&
    JSON.stringify(composite.vectorProfiles) === JSON.stringify(vectors), AGGREGATE.errors.proof);
  const proofRoot = paths['proof-root'];
  const { controlProof, scaleProofs, vectorProofs } = await readProofs(proofRoot, control, scales, vectors);
  const controlInput = path.join(paths.input, 'data');
  const controlProofPath = path.join(proofRoot, 'proof.json');
  await aggregateEvidence({ input: controlInput, output: paths.output, plan: paths.plan, proof: controlProofPath });
  const manifests = await publishScaleProfiles(paths.input, proofRoot, scaleOutput, scales, scaleProofs);
  const vectorManifests = await publishVectorProfiles(paths.input, proofRoot, scaleOutput, vectors, vectorProofs);
  const controlManifest = await readJson(path.join(paths.output, AGGREGATE.manifest), AGGREGATE.metadataBytes);
  const controlHash = (await hashRegularFile(path.join(paths.output, AGGREGATE.manifest), AGGREGATE.metadataBytes)).sha256;
  const receipt = createScaleCohortReceipt({ control: controlManifest, controlProof, controlHash,
    plans: scales, manifests, proofs: scaleProofs, vectorPlans: vectors, vectorManifests, vectorProofs, contract });
  await writeJson(path.join(scaleOutput, 'cohort-receipt.json'), receipt);
  for (const plan of scales) await cp(path.join(scaleOutput, plan.profile), path.join(paths.output, 'scaled', plan.profile), { recursive: true, errorOnExist: true });
  await cp(path.join(scaleOutput, 'vector'), path.join(paths.output, 'vector'), { recursive: true, errorOnExist: true });
  await writeJson(path.join(paths.output, 'cohort-receipt.json'), receipt);
  return receipt;
}

export async function aggregateScaleCohort(paths) {
  const scaleOutput = await createDirectory(paths['scale-output']);
  try {
    return await aggregateScaleCohortCore(paths, scaleOutput);
  } catch (error) {
    const known = Object.values(AGGREGATE.errors).includes(error.code);
    const diagnostic = { schemaVersion: 1, status: 'incomplete',
      error: known ? error.code : AGGREGATE.errors.input };
    await writeJson(path.join(scaleOutput, 'failed-accounting.json'), diagnostic).catch(() => undefined);
    throw error;
  }
}

const executed = process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href;
if (executed) {
  try {
    const paths = parseArguments(process.argv.slice(2));
    const receipt = await aggregateScaleCohort(paths);
    process.stdout.write(JSON.stringify({ schemaVersion: receipt.schemaVersion,
      qualified: receipt.qualified, scaledProfiles: receipt.scaledProfiles.length,
      vectorProfiles: receipt.vectorProfiles.length }) + '\n');
  } catch (error) {
    const known = Object.values(AGGREGATE.errors).includes(error.code);
    process.stderr.write(JSON.stringify({ error: known ? error.code : AGGREGATE.errors.input }) + '\n');
    process.exitCode = 1;
  }
}
