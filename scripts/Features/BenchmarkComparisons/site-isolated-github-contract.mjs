import { exactKeys } from './aggregate-contracts.mjs';

export const SITE_GH = Object.freeze({
  repository: 'managedcode/KeyLoad', repositoryId: 477801965,
  executor: 'Benchmarks', executorPath: '.github/workflows/benchmarks.yml',
  executorJobs: Object.freeze(['qualify', 'deploy']), producerEvents: Object.freeze(['push', 'workflow_dispatch']),
  producerKeys: Object.freeze(['runId', 'attempt', 'sourceRevision']), pinnedRunCapture: 'requested-run.json',
  aggregateJob: 'Combine benchmark results', suite: 'comparison-isolated-suite', provider: 'comparison-isolated-provider-evidence',
  metadata: 'metadata', archives: 'archives', input: 'input', metadataProof: 'metadata-proof.json', receipt: 'archive-receipt.json',
  metadataState: 'metadata_verified', archiveState: 'archive_verified', publish: 'publish', validate: 'validate',
  suiteBytes: 19_327_352_832, providerBytes: 134_217_728, jsonBytes: 4_194_304,
  metadataBytes: 16_777_216, rawBytes: 67_108_864, totalRawBytes: 17_179_869_184,
  pairs: 2000, items: 2000, files: 277, workers: 270, version: 1,
  failure: 'Isolated Pages GitHub evidence rejected.',
  steps: Object.freeze(['Verify benchmark plan',
    'Download benchmark results', 'Check all 270 benchmark results',
    'Generate website benchmark data', 'Save website benchmark data',
    'Save combined benchmark results', 'Save GitHub result verification']),
  receiptKeys: Object.freeze(['schemaVersion', 'state', 'mode', 'publishEligible', 'source', 'repository', 'workflow', 'run',
    'cohort', 'aggregateJob', 'artifacts', 'workers', 'image', 'metadataFiles', 'archives', 'inputFiles']),
  artifactKeys: Object.freeze(['id', 'name', 'sizeInBytes', 'digest', 'expired', 'createdAt']),
  fileKeys: Object.freeze(['path', 'bytes', 'sha256']),
  providerFiles: Object.freeze(['proof.json', 'image-proof.json', 'images/image-bundle.json', 'images/image-receipt.json',
    'images/server-manifest.json', 'images/comparisons-manifest.json']),
});

export const exact = exactKeys;
export function requireSite(condition) { if (!condition) throw new Error(SITE_GH.failure); }
export const safeRelative = value => typeof value === 'string' && /^[A-Za-z0-9_.-]+(?:\/[A-Za-z0-9_.-]+)*$/.test(value)
  && !value.split('/').some(part => part === '.' || part === '..');
export const artifactLimit = name => name === SITE_GH.suite ? SITE_GH.suiteBytes : SITE_GH.providerBytes;
export const projectSiteArtifact = item => ({ id: item.id, name: item.name, sizeInBytes: item.size_in_bytes,
  digest: item.digest, expired: item.expired, createdAt: item.created_at });
export const plainArtifact = ({ createdAt, ...artifact }) => artifact;
