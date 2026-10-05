import { compositeSuiteFiles, compositeProviderFiles } from './composite-site-contract.mjs';
import { compositeSitePlans } from './composite-site-contract.mjs';
import { HISTORICAL, createHistoricalPlan, isHistoricalSource } from './historical-isolated-plan.mjs';
import { exactKeys } from './aggregate-contracts.mjs';

export const SITE_GH = Object.freeze({
  repository: 'managedcode/KeyLoad', repositoryId: 477801965,
  executor: 'CI', executorPath: '.github/workflows/ci.yml',
  executorEvents: Object.freeze(['push', 'workflow_dispatch', 'workflow_run']),
  executorJobs: Object.freeze(['qualify', 'deploy']), producerEvents: Object.freeze(['push', 'workflow_dispatch']),
  producerKeys: Object.freeze(['runId', 'attempt', 'sourceRevision', 'event', 'conclusion']),
  producerConclusions: Object.freeze(['success', 'failure']), eventBytes: 1_048_576, pinnedRunCapture: 'requested-run.json',
  aggregateJob: 'Combine benchmark results', suite: 'comparison-isolated-suite', provider: 'comparison-isolated-provider-evidence',
  metadata: 'metadata', archives: 'archives', input: 'input', metadataProof: 'metadata-proof.json', receipt: 'archive-receipt.json',
  metadataState: 'metadata_verified', archiveState: 'archive_verified', publish: 'publish', validate: 'validate',
  suiteBytes: 19_327_352_832, providerBytes: 134_217_728, jsonBytes: 4_194_304,
  metadataBytes: 16_777_216, rawBytes: 67_108_864, totalRawBytes: 17_179_869_184,
  pairs: 2000, items: 2000, files: compositeSuiteFiles().length + compositeProviderFiles().length, workers: 1386, version: 1,
  failure: 'Isolated Pages GitHub evidence rejected.',
  steps: Object.freeze(['Verify benchmark plan',
    'Download benchmark results', 'Check control and complete scale accounting',
    'Save combined benchmark results', 'Save internal scaled cohort receipt', 'Save GitHub result verification']),
  legacySteps: Object.freeze(['Verify benchmark plan',
    'Download benchmark results', 'Check all 270 benchmark results',
    'Generate website benchmark data', 'Save website benchmark data',
    'Save combined benchmark results', 'Save GitHub result verification']),
  legacySources: HISTORICAL.sourceRevisions,
  receiptKeys: Object.freeze(['schemaVersion', 'state', 'mode', 'publishEligible', 'source', 'repository', 'workflow', 'run',
    'cohort', 'aggregateJob', 'artifacts', 'workers', 'image', 'metadataFiles', 'archives', 'inputFiles']),
  artifactKeys: Object.freeze(['id', 'name', 'sizeInBytes', 'digest', 'expired', 'createdAt']),
  fileKeys: Object.freeze(['path', 'bytes', 'sha256']),
  providerFiles: Object.freeze(compositeProviderFiles()),
});

const legacyProviderFiles = Object.freeze(['provider/proof.json', 'provider/image-proof.json',
  'provider/images/image-bundle.json', 'provider/images/image-receipt.json',
  'provider/images/server-manifest.json', 'provider/images/comparisons-manifest.json']);
export const siteEvidencePlans = source => isHistoricalSource(source) ? [createHistoricalPlan(source)] : compositeSitePlans();
export const siteEvidenceSuiteFiles = source => SITE_GH.legacySources.includes(source)
  ? ['aggregate/aggregate.json', ...createHistoricalPlan(source).cells.map(cell => `aggregate/workers/${cell.id}/worker.json`)]
  : compositeSuiteFiles().map(file => `aggregate/${file}`);
export const siteEvidenceProviderFiles = source => SITE_GH.legacySources.includes(source) ? legacyProviderFiles : compositeProviderFiles().map(file => `provider/${file}`);
export const siteEvidenceWorkerCount = source => siteEvidencePlans(source).reduce((count, plan) => count + plan.cells.length, 0);

export const exact = exactKeys;
export function requireSite(condition) { if (!condition) throw new Error(SITE_GH.failure); }
export const safeRelative = value => typeof value === 'string' && /^[A-Za-z0-9_.-]+(?:\/[A-Za-z0-9_.-]+)*$/.test(value)
  && !value.split('/').some(part => part === '.' || part === '..');
export const artifactLimit = name => name === SITE_GH.suite ? SITE_GH.suiteBytes : SITE_GH.providerBytes;
export const projectSiteArtifact = item => ({ id: item.id, name: item.name, sizeInBytes: item.size_in_bytes,
  digest: item.digest, expired: item.expired, createdAt: item.created_at });
export const plainArtifact = ({ createdAt, ...artifact }) => artifact;
export const siteAggregateSteps = source => SITE_GH.legacySources.includes(source) ? SITE_GH.legacySteps : SITE_GH.steps;
