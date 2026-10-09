export { isolatedEvidenceJobName, isolatedJobName, matchesIsolatedJobName } from '../../../site/Features/BenchmarkComparisons/isolated-contracts.mjs';

export const GH = Object.freeze({
  repository: 'managedcode/KeyLoad', workflow: 'Benchmarks', workflowPath: '.github/workflows/benchmarks.yml',
  api: 'repos/managedcode/KeyLoad/actions', host: 'github.com', apiVersion: '2022-11-28',
  imageJob: 'Build Docker images', imageArtifact: 'comparison-image-bundle', casePrefix: 'Benchmark / ',
  artifactPrefix: 'comparison-worker-', captureDirectory: 'keyload-cell-github',
  imageSteps: Object.freeze(['Check Docker image export and import', 'Save Docker images']),
  workerSteps: Object.freeze(['Run database workload', 'Save benchmark results']),
  pageSize: 100, pages: 40, items: 4000, metadataBytes: 16777216, workerZipBytes: 134217728,
  workerRawBytes: 67108864, serverResourceBytes: 65536, imageZipBytes: 9663676416, totalWorkerZipBytes: 17179869184,
  metadataTimeoutMs: 120000, downloadTimeoutMs: 600000, unzipTimeoutMs: 120000,
  headerBytes: 65536, rateWaitMs: 3700000, rateRepeats: 3,
  stderrBytes: 262144, inventoryBytes: 1048576, inventoryEntries: 4096,
  failure: 'Isolated GitHub evidence rejected.',
});

export function requireGitHub(condition) {
  if (!condition) throw new Error(GH.failure);
}

export const positive = value => Number.isSafeInteger(value) && value > 0;
export const canonicalJobUrl = (cohort, id) => `https://github.com/${cohort.repository}/actions/runs/${cohort.runId}/job/${id}`;
export const hashPattern = /^[a-f0-9]{64}$/;
export const shaPattern = /^[a-f0-9]{40}$/;
export const digestPattern = /^sha256:[a-f0-9]{64}$/;

export function timestamp(value) {
  requireGitHub(typeof value === 'string' && /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?(?:Z|[+-]\d\d:\d\d)$/.test(value));
  const parsed = Date.parse(value);
  requireGitHub(Number.isFinite(parsed));
  return parsed;
}
