// UI identity and bounded browser errors; session owns the only measurement wire schema.
export const ISOLATED = Object.freeze({ repository: 'managedcode/KeyLoad', ref: 'refs/heads/main', workflow: 'Benchmarks',
  error: 'E_ISOLATED_EVIDENCE', failed: 'failed', failure: 'failure', success: 'success' });
export const DOM = Object.freeze({
  scenario: 'isolated-scenario', nodes: 'isolated-node-count', target: 'isolated-target', metric: 'isolated-metric',
  repetition: 'isolated-repetition', results: 'isolated-results', error: 'isolated-error', retry: 'isolated-retry',
  announcement: 'isolated-announcement',
});
export function assertIsolated(condition) {
  if (!condition) {
    const error = new TypeError('Benchmark session evidence is unavailable or invalid.');
    error.code = ISOLATED.error;
    throw error;
  }
}
export const runUrl = cohort => `https://github.com/${ISOLATED.repository}/actions/runs/${cohort.runId}`;
export const artifactUrl = (cohort, artifact) => `${runUrl(cohort)}/artifacts/${artifact.id}`;
