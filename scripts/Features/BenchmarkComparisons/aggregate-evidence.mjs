import { writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { validateServerResourceEvidence } from './server-resource-evidence.mjs';
import { AGGREGATE, requireValue } from './aggregate-contracts.mjs';
import { readIsolatedContract, validateIsolatedPlan } from './isolated-plan.mjs';
import { requireWorkerJobAgreement, validateAggregateProof, validateWorkerEnvelope } from './aggregate-validation.mjs';
import { createStage, hashBytes, parseBytes, publishStage, rawFile, rawPath, readBytes, removeStage,
  retainBytes, validateInventory, validatePaths } from './aggregate-files.mjs';
import { validateScaledPlan } from './scaled-isolated-plan.mjs';
import { validateVectorPlan } from './vector-isolated-plan.mjs';

function retainCommonFacts(common, report) {
  if (report === null) return;
  const facts = { datasetSha256: report.datasetSha256, architecture: report.architecture,
    loadGeneratorImage: report.loadGeneratorImage, loadModel: report.loadModel };
  for (const [key, value] of Object.entries(facts)) {
    requireValue(common[key] === undefined || common[key] === value, AGGREGATE.errors.cohort);
    common[key] = value;
  }
  const target = report.targets[0];
  const identity = { image: target.image, version: target.version, transport: target.transport, authorization: target.authorization };
  const previous = common.targets.get(target.name);
  requireValue(previous === undefined || Object.entries(identity).every(([key, value]) => previous[key] === value), AGGREGATE.errors.cohort);
  common.targets.set(target.name, identity);
}

function manifestWorker(cell, envelope, proof, hash) {
  const worker = { id: cell.id, target: cell.target, nodeCount: cell.nodeCount, scenario: cell.scenario, profile: cell.profile,
    disposition: envelope.disposition, reason: envelope.reason, rawPath: rawPath(cell.id), rawSha256: hash,
    job: proof.job, artifact: proof.artifact };
  if (cell.profile.startsWith('vector-')) {
    const metrics = envelope.report?.cases?.[0]?.vectorMetrics ?? null;
    worker.vectorMetrics = metrics === null ? null : Object.fromEntries(Object.entries(metrics).filter(([key]) => key !== 'perQueryRecall'));
  }
  return worker;
}

async function retainWorker(input, stage, cell, proof, cohort, contract, common) {
  const bytes = await readBytes(rawFile(input, cell.id), AGGREGATE.workerBytes);
  const hash = hashBytes(bytes);
  requireValue(hash === proof.workerSha256, AGGREGATE.errors.proof);
  const envelope = validateWorkerEnvelope(parseBytes(bytes), cell, cohort, contract);
  requireValue(envelope.worker.jobId === proof.job.id, AGGREGATE.errors.proof);
  requireWorkerJobAgreement(envelope, proof.job);
  retainCommonFacts(common, envelope.report);
  common.bytes += bytes.length;
  requireValue(common.bytes <= AGGREGATE.totalBytes, AGGREGATE.errors.input);
  await retainBytes(stage, cell.id, bytes);
  if (proof.serverResource !== undefined) {
    const sidecar = await readBytes(join(input, 'workers', cell.id, 'server-resource-evidence.json'), 65_536);
    const resource = validateServerResourceEvidence(parseBytes(sidecar), hashBytes(sidecar), hash,
      cell, cohort, proof.job.id, envelope.disposition !== 'unsupportedTopology');
    requireValue(isDeepStrictEqual(resource, proof.serverResource), AGGREGATE.errors.proof);
    await writeFile(join(stage, 'workers', cell.id, 'server-resource-evidence.json'), sidecar, { flag: 'wx' });
  }
  return manifestWorker(cell, envelope, proof, hash);
}

// Supplied proof must already be authenticated by the workflow's GitHub/ZIP transport.
export async function aggregateEvidence({ input, output, plan, proof }) {
  await validatePaths({ input, output, plan, proof });
  const contract = readIsolatedContract();
  const requestedPlan = parseBytes(await readBytes(plan, AGGREGATE.metadataBytes));
  const inventory = requestedPlan?.profile === contract.profile ? validateIsolatedPlan(requestedPlan, contract)
    : requestedPlan?.profile?.startsWith('vector-') ? validateVectorPlan(requestedPlan, contract)
      : validateScaledPlan(requestedPlan, contract);
  const profileContract = inventory.profile === contract.profile ? contract
    : { ...contract, profile: inventory.profile, options: inventory.profileSettings ?? null };
  const evidence = validateAggregateProof(parseBytes(await readBytes(proof, AGGREGATE.metadataBytes)), inventory);
  await validateInventory(input, inventory.cells);
  const byId = new Map(evidence.cells.map(cell => [cell.id, cell]));
  const stage = await createStage(output);
  try {
    const common = { bytes: 0, targets: new Map() };
    const workers = [];
    for (const cell of inventory.cells) {
      workers.push(await retainWorker(input, stage, cell, byId.get(cell.id), evidence.cohort, profileContract, common));
    }
    const manifest = inventory.profileSettings === undefined
      ? { schemaVersion: AGGREGATE.version, cohort: evidence.cohort, profile: inventory.profile,
        options: inventory.options, datasetSha256: common.datasetSha256 ?? null, workers }
      : inventory.profile.startsWith('vector-')
        ? { schemaVersion: AGGREGATE.version, cohort: evidence.cohort, profile: inventory.profile,
          vectorProfile: inventory.profileSettings, datasetSha256: common.datasetSha256 ?? null, workers }
        : { schemaVersion: AGGREGATE.version, cohort: evidence.cohort, profile: inventory.profile,
        profileSettings: inventory.profileSettings, datasetSha256: common.datasetSha256 ?? null, workers };
    await publishStage(stage, output, manifest);
    return manifest;
  } catch (error) {
    await removeStage(stage);
    throw error;
  }
}
