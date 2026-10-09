import { compositeSuiteFiles, compositeProviderFiles, compositeSitePlans } from './composite-site-contract.mjs';
import { exactKeys } from './aggregate-contracts.mjs';

const unsupportedSourceRevisions = Object.freeze([
  'c16a1d928d7d6941db74403e47f3dbcea206d86a',
  'ce2eace916b3660a4c7fe2976a012637600c3b28',
  'fb586bfcf05c18e79f892c5ecaf5c1092811aaf8',
  '55fb4e3704bd30b5e57b8173953e6e017c0f8333',
  '1e8833c027cf232e35fe012cd3eed41c61a17f89',
  'a3136fb90f7cbaaad8935fdc04984372167e6d4c',
  'f8b3ba68da2f28660da1a36db396882ba2f72b7d',
  '5fa61f27a34b5c573579fca3a84b8e5201dbd82e',
  '3985008b0d360dd7e1bd9d26aae572e5e6368128',
  '37a9da0394b7219b4fa05edd21ee41ec614e604f',
  'bb16152827b38dc4533a1d7830e664b4ecd11267',
  '377886f35928866f083806062b446056d64539e3',
  '873cd1a36ad14ab966065c924c71a292ea681083',
  '0d78eb43dceac2f386dca7bbccb11f9d1e3d43a3',
  'fabff69193f41c784f0b36b85c1f34d82fa9903d',
  'aa49aa93982866b85a80e771a749d9b968ffc9f2',
  '73aebfd3f72695357834599e813aba77b9e274ad',
  '77167cbca9efe8942aab869dd52ad5b0b6cc72a1',
  'ff0af70a279b6adce653bc5fe2527fef51f9de69',
]);

export const SITE_GH = Object.freeze({
  repository: 'managedcode/KeyLoad', repositoryId: 477801965,
  executor: 'Website', executorPath: '.github/workflows/website.yml',
  executorEvents: Object.freeze(['push', 'workflow_dispatch']),
  executorJobs: Object.freeze(['qualify', 'deploy']), producerEvents: Object.freeze(['push', 'workflow_dispatch']),
  producerKeys: Object.freeze(['runId', 'attempt', 'sourceRevision', 'event', 'conclusion']),
  producerConclusions: Object.freeze(['success', 'failure']), pinnedRunCapture: 'requested-run.json',
  aggregateJob: 'Combine benchmark results', suite: 'comparison-isolated-suite', provider: 'comparison-isolated-provider-evidence',
  metadata: 'metadata', archives: 'archives', input: 'input', metadataProof: 'metadata-proof.json', receipt: 'archive-receipt.json',
  metadataState: 'metadata_verified', archiveState: 'archive_verified', publish: 'publish', validate: 'validate',
  suiteBytes: 19_327_352_832, providerBytes: 134_217_728, jsonBytes: 4_194_304,
  metadataBytes: 16_777_216, rawBytes: 67_108_864, totalRawBytes: 17_179_869_184,
  pairs: 2000, items: 2000, files: compositeSuiteFiles().length + compositeProviderFiles().length, workers: 924, version: 1,
  failure: 'Isolated Pages GitHub evidence rejected.',
  steps: Object.freeze(['Verify benchmark plan',
    'Download benchmark results', 'Check control and complete scale accounting',
    'Save combined benchmark results', 'Save internal scaled cohort receipt', 'Save GitHub result verification']),
  unsupportedSourceRevisions,
  receiptKeys: Object.freeze(['schemaVersion', 'state', 'mode', 'publishEligible', 'source', 'repository', 'workflow', 'run',
    'cohort', 'aggregateJob', 'artifacts', 'workers', 'image', 'metadataFiles', 'archives', 'inputFiles']),
  artifactKeys: Object.freeze(['id', 'name', 'sizeInBytes', 'digest', 'expired', 'createdAt']),
  fileKeys: Object.freeze(['path', 'bytes', 'sha256']),
  providerFiles: Object.freeze(compositeProviderFiles()),
});

export const isUnsupportedSiteSource = sourceRevision => unsupportedSourceRevisions.includes(sourceRevision);
export const siteEvidencePlans = () => compositeSitePlans();
export const siteEvidenceSuiteFiles = () => compositeSuiteFiles().map(file => `aggregate/${file}`);
export const siteEvidenceProviderFiles = () => compositeProviderFiles().map(file => `provider/${file}`);
export const siteEvidenceWorkerCount = () => siteEvidencePlans().reduce((count, plan) => count + plan.cells.length, 0);

export const exact = exactKeys;
export function requireSite(condition) { if (!condition) throw new Error(SITE_GH.failure); }
export const safeRelative = value => typeof value === 'string' && /^[A-Za-z0-9_.-]+(?:\/[A-Za-z0-9_.-]+)*$/.test(value)
  && !value.split('/').some(part => part === '.' || part === '..');
export const artifactLimit = name => name === SITE_GH.suite ? SITE_GH.suiteBytes : SITE_GH.providerBytes;
export const projectSiteArtifact = item => ({ id: item.id, name: item.name, sizeInBytes: item.size_in_bytes,
  digest: item.digest, expired: item.expired, createdAt: item.created_at });
export const plainArtifact = ({ createdAt, ...artifact }) => artifact;
export const siteAggregateSteps = () => SITE_GH.steps;
