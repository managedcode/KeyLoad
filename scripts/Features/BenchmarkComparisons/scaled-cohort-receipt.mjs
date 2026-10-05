import { isDeepStrictEqual } from 'node:util';
import { AGGREGATE, exactKeys, requireValue } from './aggregate-contracts.mjs';
import { scaleProfileSettings, SCALED_PROFILES } from './scaled-isolated-plan.mjs';
import { vectorProfileSettings, VECTOR_PROFILES } from './vector-isolated-plan.mjs';
import { SERVER_RESOURCE_MISSING_KINDS, requireComparableScaleProfiles } from './server-resource-evidence.mjs';

export const SCALE_RECEIPT_MISSING = SERVER_RESOURCE_MISSING_KINDS;
const RECEIPT_ERROR = AGGREGATE.errors.proof;
const COMMON_FIELDS = Object.freeze(['sourceRevision', 'runId', 'attempt', 'repository', 'ref', 'workflow']);
const CONTROL_ROW_FIELDS = Object.freeze(['id', 'artifactId', 'artifactName', 'artifactDigest', 'workerSha256']);
const SCALE_ROW_FIELDS = Object.freeze(['id', 'target', 'nodeCount', 'scenario', 'disposition', 'artifactId',
  'artifactName', 'artifactDigest', 'workerSha256', 'serverResourceSha256', 'serverResourceQualified',
  'serverResourceMissingEvidence', 'resourceEquivalence']);
const VECTOR_ROW_FIELDS = Object.freeze([...SCALE_ROW_FIELDS, 'serverMemoryBytes', 'serverMemorySampleCount', 'vectorMetrics']);

function proofRows(proof, manifest) {
  requireValue(Array.isArray(proof?.cells) && Array.isArray(manifest?.workers) &&
    proof.cells.length === manifest.workers.length, RECEIPT_ERROR);
  const proofMap = new Map(proof.cells.map(item => [item.id, item]));
  requireValue(proofMap.size === proof.cells.length, RECEIPT_ERROR);
  return manifest.workers.map(worker => {
    const item = proofMap.get(worker.id);
    requireValue(item !== undefined && worker.rawSha256 === item.workerSha256 &&
      (worker.disposition === 'failed') === (item.job.conclusion === 'failure'), RECEIPT_ERROR);
    return { worker, proof: item };
  });
}

function artifactRow(id, proof) {
  const artifact = proof.artifact;
  requireValue(Number.isSafeInteger(artifact.id) && artifact.id > 0 &&
    artifact.name === `comparison-worker-${id}` && /^sha256:[a-f0-9]{64}$/u.test(artifact.digest) &&
    proof.workerSha256 === proof.workerSha256.toLowerCase() && /^[a-f0-9]{64}$/u.test(proof.workerSha256), RECEIPT_ERROR);
  return { artifactId: artifact.id, artifactName: artifact.name, artifactDigest: artifact.digest,
    workerSha256: proof.workerSha256 };
}

function validateManifest(manifest, profile, expectedCount) {
  requireValue(manifest?.profile === profile && Array.isArray(manifest.workers) &&
    manifest.workers.length === expectedCount && manifest.workers.every(worker => worker.profile === profile), RECEIPT_ERROR);
}

function validateGlobalIdentities(proofs) {
  requireValue(proofs.every(proof => Array.isArray(proof?.cells)), RECEIPT_ERROR);
  for (const project of ['job', 'artifact']) {
    const ids = proofs.flatMap(proof => proof.cells.map(item => item[project].id));
    requireValue(ids.length === 1_386 && new Set(ids).size === ids.length, RECEIPT_ERROR);
  }
}

function controlReceipt(control, controlProof, controlHash) {
  validateManifest(control, 'intensive-1k-c16', 330);
  const rows = proofRows(controlProof, control).map(({ worker, proof }) => ({ id: worker.id, ...artifactRow(worker.id, proof) }));
  requireValue(rows.every(row => exactKeys(row, CONTROL_ROW_FIELDS)) && /^[a-f0-9]{64}$/u.test(controlHash), RECEIPT_ERROR);
  return { profile: control.profile, cellCount: rows.length, aggregateSha256: controlHash, cells: rows };
}

function validateDisposition(worker, unsupportedTopologies) {
  if (worker.disposition === 'failed') return;
  const unsupported = unsupportedTopologies.find(item => item.target === worker.target && item.nodeCounts.includes(worker.nodeCount));
  if (unsupported) {
    requireValue(worker.disposition === 'unsupportedTopology' && worker.reason === unsupported.reason, RECEIPT_ERROR);
    return;
  }
  requireValue(['measured', 'failed'].includes(worker.disposition), RECEIPT_ERROR);
}

function scaledReceipt(plan, manifest, proof, contract, failedIds) {
  validateManifest(manifest, plan.profile, 132);
  const byId = new Map(manifest.workers.map(worker => [worker.id, worker]));
  requireValue(byId.size === 132, RECEIPT_ERROR);
  const proofs = new Map(proof.cells.map(item => [item.id, item]));
  requireValue(proofs.size === 132, RECEIPT_ERROR);
  const rows = plan.cells.map(cell => {
    const worker = byId.get(cell.id);
    const item = proofs.get(cell.id);
    requireValue(worker !== undefined && item !== undefined && worker.target === cell.target &&
      worker.nodeCount === cell.nodeCount && worker.scenario === cell.scenario && worker.profile === cell.profile &&
      worker.rawSha256 === item.workerSha256 &&
      (worker.disposition === 'failed') === (item.job.conclusion === 'failure'), RECEIPT_ERROR);
    validateDisposition(worker, contract.unsupportedTopologies);
    if (worker.disposition === 'failed') failedIds.push(worker.id);
    const resource = item.serverResource;
    requireValue(resource !== undefined && /^[a-f0-9]{64}$/u.test(resource.sha256)
      && typeof resource.qualified === 'boolean' && Array.isArray(resource.missingEvidence)
      && resource.missingEvidence.every(name => SERVER_RESOURCE_MISSING_KINDS.includes(name))
      && ((worker.disposition === 'unsupportedTopology' || !resource.qualified)
        ? resource.comparison === null : resource.comparison !== null),
    RECEIPT_ERROR);
    const row = { id: cell.id, target: cell.target, nodeCount: cell.nodeCount, scenario: cell.scenario,
      disposition: worker.disposition, ...artifactRow(cell.id, item), serverResourceSha256: resource.sha256,
      serverResourceQualified: resource.qualified, serverResourceMissingEvidence: resource.missingEvidence,
      resourceEquivalence: resource.comparison };
    requireValue(exactKeys(row, SCALE_ROW_FIELDS), RECEIPT_ERROR);
    return row;
  });
  return { id: plan.profile, settings: plan.profileSettings, cellCount: rows.length, cells: rows };
}

function vectorReceipt(plan, manifest, proof, contract, failedIds) {
  validateManifest(manifest, plan.profile, 33);
  const byId = new Map(manifest.workers.map(worker => [worker.id, worker]));
  const proofs = new Map(proof.cells.map(item => [item.id, item]));
  requireValue(byId.size === 33 && proofs.size === 33, RECEIPT_ERROR);
  const rows = plan.cells.map(cell => {
    const worker = byId.get(cell.id);
    const item = proofs.get(cell.id);
    requireValue(worker !== undefined && item !== undefined && worker.target === cell.target &&
      worker.nodeCount === cell.nodeCount && worker.scenario === 'VectorExact' && worker.profile === cell.profile &&
      worker.rawSha256 === item.workerSha256 && (worker.disposition === 'failed') === (item.job.conclusion === 'failure'), RECEIPT_ERROR);
    const topology = contract.unsupportedTopologies.find(entry => entry.target === cell.target && entry.nodeCounts.includes(cell.nodeCount));
    const method = plan.profileSettings.indexKind;
    const supportedMethod = cell.target === 'PostgreSQL + pgvector' ? ['Exact', 'Hnsw', 'IvfFlat'].includes(method)
      : cell.target === 'Qdrant' || cell.target === 'SurrealDB' ? ['Exact', 'Hnsw'].includes(method)
        : cell.target === 'HelixDB' && method === 'NativeAnn';
    if (worker.disposition === 'failed') {
      requireValue(worker.vectorMetrics === null, RECEIPT_ERROR);
    } else if (topology !== undefined) {
      requireValue(worker.disposition === 'unsupportedTopology' && worker.reason === topology.reason
        && worker.vectorMetrics === null, RECEIPT_ERROR);
    } else if (cell.target === 'KeyLoad') {
      requireValue(worker.disposition === 'unsupported' && worker.reason ===
        'KeyLoad SDK does not expose persisted vector readback or native numeric predicates required for scaled vector qualification.' && worker.vectorMetrics === null, RECEIPT_ERROR);
    } else if (!supportedMethod) {
      requireValue(worker.disposition === 'unsupported' && worker.reason ===
        `${cell.target} does not implement ${method}/${plan.profileSettings.queryMode} natively.`
        && worker.vectorMetrics === null, RECEIPT_ERROR);
    } else {
      requireValue(worker.disposition === 'failed' ? worker.vectorMetrics === null : worker.disposition === 'measured' && worker.vectorMetrics !== null &&
        worker.vectorMetrics.recordCount === plan.profileSettings.recordCount &&
        worker.vectorMetrics.loadedRecordCount === plan.profileSettings.recordCount &&
        worker.vectorMetrics.queryAttempts === plan.profileSettings.measuredQueries &&
        worker.vectorMetrics.querySuccesses === plan.profileSettings.measuredQueries &&
        worker.vectorMetrics.indexKind === method && worker.vectorMetrics.exactRecall >= plan.profileSettings.minimumRecall,
      RECEIPT_ERROR);
    }
    if (worker.disposition === 'failed') failedIds.push(worker.id);
    const resource = item.serverResource;
    requireValue(resource !== undefined && /^[a-f0-9]{64}$/u.test(resource.sha256)
      && typeof resource.qualified === 'boolean' && Array.isArray(resource.missingEvidence)
      && resource.missingEvidence.every(name => SERVER_RESOURCE_MISSING_KINDS.includes(name)), RECEIPT_ERROR);
    const row = { id: cell.id, target: cell.target, nodeCount: cell.nodeCount, scenario: cell.scenario,
      disposition: worker.disposition, ...artifactRow(cell.id, item), serverResourceSha256: resource.sha256,
      serverResourceQualified: resource.qualified, serverResourceMissingEvidence: resource.missingEvidence,
      resourceEquivalence: resource.comparison,
      serverMemoryBytes: worker.disposition === 'measured' ? resource.observedServerMemory?.bytes ?? null : null,
      serverMemorySampleCount: worker.disposition === 'measured' ? resource.observedServerMemory?.sampleCount ?? null : null,
      vectorMetrics: worker.vectorMetrics };
    requireValue(exactKeys(row, VECTOR_ROW_FIELDS), RECEIPT_ERROR);
    return row;
  });
  return { id: plan.profile, settings: plan.profileSettings, cellCount: rows.length, cells: rows };
}

export function createScaleCohortReceipt({ control, controlProof, controlHash, plans, manifests, proofs,
  vectorPlans = [], vectorManifests = [], vectorProofs = [], contract }) {
  requireValue(Array.isArray(plans) && plans.length === SCALED_PROFILES.length &&
    Array.isArray(manifests) && manifests.length === plans.length && Array.isArray(proofs) && proofs.length === plans.length &&
    Array.isArray(vectorPlans) && vectorPlans.length === VECTOR_PROFILES.length && Array.isArray(vectorManifests) &&
    vectorManifests.length === vectorPlans.length && Array.isArray(vectorProofs) && vectorProofs.length === vectorPlans.length,
  RECEIPT_ERROR);
  validateGlobalIdentities([controlProof, ...proofs, ...vectorProofs]);
  const common = controlProof.cohort;
  requireValue(COMMON_FIELDS.every(field => control.cohort?.[field] === common[field]), RECEIPT_ERROR);
  const failedIds = [];
  const scaleProfiles = plans.map((plan, index) => {
    const profile = SCALED_PROFILES[index];
    requireValue(plan.profile === profile.id && isDeepStrictEqual(plan.profileSettings, scaleProfileSettings(profile.id)) &&
      proofs[index].cohort.profile === profile.id && manifests[index].cohort?.profile === profile.id &&
      COMMON_FIELDS.every(field => proofs[index].cohort[field] === common[field] && manifests[index].cohort?.[field] === common[field]),
    RECEIPT_ERROR);
    return scaledReceipt(plan, manifests[index], proofs[index], contract, failedIds);
  });
  const vectorProfiles = vectorPlans.map((plan, index) => {
    const expected = VECTOR_PROFILES[index];
    requireValue(plan.profile === expected.id && isDeepStrictEqual(plan.profileSettings, vectorProfileSettings(expected.id)) &&
      vectorProofs[index].cohort.profile === expected.id && vectorManifests[index].cohort?.profile === expected.id &&
      COMMON_FIELDS.every(field => vectorProofs[index].cohort[field] === common[field]
        && vectorManifests[index].cohort?.[field] === common[field]), RECEIPT_ERROR);
    return vectorReceipt(plan, vectorManifests[index], vectorProofs[index], contract, failedIds);
  });
  const allProfiles = [...scaleProfiles, ...vectorProfiles];
  requireComparableScaleProfiles(allProfiles.map(profile => ({
    profile: profile.id,
    workers: profile.cells.map(cell => ({ target: cell.target, nodeCount: cell.nodeCount,
      scenario: cell.scenario, disposition: cell.disposition,
      serverResourceQualified: cell.serverResourceQualified, resourceEquivalence: cell.resourceEquivalence }))
  })));
  const sortedFailures = [...failedIds].sort();
  const missingEvidence = [...new Set(allProfiles.flatMap(profile => profile.cells
    .flatMap(cell => cell.serverResourceMissingEvidence)))].sort();
  const controlReceiptValue = controlReceipt(control, controlProof, controlHash);
  return { schemaVersion: 1, cohort: Object.fromEntries(COMMON_FIELDS.map(field => [field, common[field]])),
    control: controlReceiptValue, scaledProfiles: scaleProfiles, vectorProfiles,
    qualified: missingEvidence.length === 0 && sortedFailures.length === 0,
    failedIds: sortedFailures, missingEvidence };
}
