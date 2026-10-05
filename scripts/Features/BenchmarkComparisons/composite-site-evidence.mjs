import path from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { AGGREGATE, exactKeys, requireValue } from './aggregate-contracts.mjs';
import { readIsolatedContract } from './isolated-plan.mjs';
import { hashBytes, readBytes, parseBytes } from './aggregate-files.mjs';
import { validateAggregateProof } from './aggregate-proof.mjs';
import { validateWorkerEnvelope, requireWorkerJobAgreement } from './aggregate-validation.mjs';
import { validateServerResourceEvidence } from './server-resource-evidence.mjs';
import { createScaleCohortReceipt } from './scaled-cohort-receipt.mjs';
import { validateCompositePlan } from './scaled-isolated-plan.mjs';
import { compositeSitePlans, familyRoot } from './composite-site-contract.mjs';
import { retainCommonFacts } from '../../../site/Features/BenchmarkComparisons/isolated-report-validation.mjs';

const check = condition => requireValue(condition, AGGREGATE.errors.proof);
const json = async file => parseBytes(await readBytes(file, AGGREGATE.metadataBytes));

async function validateFamily(input, provider, plan, receiptWorkers, expectedImages) {
  const root = path.join(input, familyRoot(plan.profile));
  const manifest = await json(path.join(root, 'aggregate.json'));
  const proof = validateAggregateProof(await json(path.join(provider, familyRoot(plan.profile), 'proof.json')), plan);
  const vector = plan.profile.startsWith('vector-');
  const settingsField = vector ? 'vectorProfile' : 'profileSettings';
  check(exactKeys(manifest, ['schemaVersion', 'cohort', 'profile', settingsField, 'datasetSha256', 'workers']) &&
    manifest.schemaVersion === AGGREGATE.version && isDeepStrictEqual(manifest[settingsField], plan.profileSettings) &&
    isDeepStrictEqual(manifest.cohort, proof.cohort) && manifest.profile === plan.profile &&
    Array.isArray(manifest.workers) && manifest.workers.length === plan.cells.length);
  const metadata = new Map(manifest.workers.map(item => [item.id, item]));
  check(metadata.size === plan.cells.length);
  const byId = new Map(proof.cells.map(item => [item.id, item]));
  const contract = { ...readIsolatedContract(), profile: plan.profile, options: plan.profileSettings };
  const cells = [];
  let dataset;
  const common = { engines: new Map(), topologies: new Map() };
  for (const cell of plan.cells) {
    const item = metadata.get(cell.id);
    const native = byId.get(cell.id);
    const trusted = receiptWorkers?.get(cell.id);
    check(item !== undefined && native !== undefined && ['target', 'nodeCount', 'scenario', 'profile'].every(key => item[key] === cell[key]) && item.rawSha256 === native.workerSha256 &&
      isDeepStrictEqual(item.job, native.job) && isDeepStrictEqual(item.artifact, native.artifact));
    check(exactKeys(item, ['id', 'target', 'nodeCount', 'scenario', 'profile', 'disposition', 'reason',
      'rawPath', 'rawSha256', 'job', 'artifact', ...(vector ? ['vectorMetrics'] : [])]) &&
      item.rawPath === `workers/${cell.id}/worker.json`);
    if (receiptWorkers) check(trusted !== undefined && isDeepStrictEqual(trusted.job, native.job));
    if (trusted) {
      const { createdAt, ...artifact } = trusted.artifact;
      check(isDeepStrictEqual(artifact, native.artifact));
    }
    const raw = await readBytes(path.join(root, 'workers', cell.id, 'worker.json'), AGGREGATE.workerBytes);
    check(hashBytes(raw) === item.rawSha256);
    const envelope = validateWorkerEnvelope(parseBytes(raw), cell, proof.cohort, contract);
    requireWorkerJobAgreement(envelope, native.job);
    check(envelope.worker.jobId === native.job.id && envelope.disposition === item.disposition && envelope.reason === item.reason);
    const sidecar = await readBytes(path.join(root, 'workers', cell.id, 'server-resource-evidence.json'), 65_536);
    const resource = validateServerResourceEvidence(parseBytes(sidecar), hashBytes(sidecar), item.rawSha256,
      cell, proof.cohort, native.job.id, envelope.disposition !== 'unsupportedTopology');
    check(isDeepStrictEqual(resource, native.serverResource));
    retainCommonFacts(common, envelope.report, cell.nodeCount);
    if (envelope.report) {
      check(dataset === undefined || dataset === envelope.report.datasetSha256);
      dataset = envelope.report.datasetSha256;
      if (expectedImages) check(envelope.report.loadGeneratorImage === expectedImages.loadGenerator &&
        (cell.target !== 'KeyLoad' || envelope.report.targets[0].image === expectedImages.server));
    }
    const metrics = envelope.report?.cases[0]?.vectorMetrics == null ? null :
      Object.fromEntries(Object.entries(envelope.report.cases[0].vectorMetrics).filter(([key]) => key !== 'perQueryRecall'));
    if (plan.profile.startsWith('vector-')) check(isDeepStrictEqual(item.vectorMetrics, metrics));
    cells.push({ ...item, nativeTarget: envelope.report?.targets[0] ?? null,
      vectorMetrics: envelope.report?.cases[0]?.vectorMetrics == null ? null :
        Object.fromEntries(Object.entries(envelope.report.cases[0].vectorMetrics).filter(([key]) => key !== 'perQueryRecall')),
      measurement: envelope.report?.cases[0]?.measurement ?? null,
      serverMemoryBytes: resource.observedServerMemory?.bytes ?? null,
      serverMemorySampleCount: resource.observedServerMemory?.sampleCount ?? null,
      resourceQualified: resource.qualified, resourceEquivalence: resource.comparison });
  }
  check(manifest.datasetSha256 === (dataset ?? null));
  return { manifest, proof, projection: { id: plan.profile, settings: plan.profileSettings, datasetSha256: dataset ?? null, cells } };
}

// Caller authenticates the provider archives and passes their original selected worker identities.
export async function validateCompositeSiteEvidence({ input, provider, receiptWorkers, images }) {
  const plans = compositeSitePlans();
  validateCompositePlan(await json(path.join(provider, 'composite-plan.json')));
  for (const plan of plans) check(isDeepStrictEqual(await json(path.join(provider, 'plans', `${plan.profile}.json`)), plan));
  const controlBytes = await readBytes(path.join(input, 'aggregate.json'), AGGREGATE.metadataBytes);
  const control = parseBytes(controlBytes);
  const controlProof = validateAggregateProof(await json(path.join(provider, 'proof.json')), plans[0]);
  const families = [];
  for (const plan of plans.slice(1)) families.push(await validateFamily(input, provider, plan, receiptWorkers, images));
  const expected = createScaleCohortReceipt({ control, controlProof, controlHash: hashBytes(controlBytes),
    plans: plans.slice(1, 3), manifests: families.slice(0, 2).map(item => item.manifest), proofs: families.slice(0, 2).map(item => item.proof),
    vectorPlans: plans.slice(3), vectorManifests: families.slice(2).map(item => item.manifest),
    vectorProofs: families.slice(2).map(item => item.proof), contract: readIsolatedContract() });
  check(isDeepStrictEqual(await json(path.join(input, 'cohort-receipt.json')), expected));
  return { schemaVersion: 1, cohort: expected.cohort, scaledProfiles: families.slice(0, 2).map(item => item.projection),
    vectorProfiles: families.slice(2).map(item => item.projection) };
}
