export const envName = Object.freeze({ githubSha: 'GITHUB_SHA', runId: 'GITHUB_RUN_ID', runAttempt: 'GITHUB_RUN_ATTEMPT',
  repository: 'GITHUB_REPOSITORY', ref: 'GITHUB_REF', runnerTemp: 'RUNNER_TEMP', workspace: 'GITHUB_WORKSPACE',
  githubOutput: 'GITHUB_OUTPUT' });
export const directoryName = Object.freeze({ evidence: 'keyload-execution-evidence' });
export const validation = Object.freeze({ shaPattern: /^[a-f0-9]{40}$/i, numericIdPattern: /^[1-9][0-9]{0,19}$/,
  repositoryPattern: /^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/, refPattern: /^refs\/[A-Za-z0-9_./-]+$/,
  maxRepositoryLength: 200, maxRefLength: 255, maxPathLength: 4096,
  controlCharacterPattern: /[\u0000-\u001f\u007f]/ });
export const processLimit = Object.freeze({ commandTimeoutMs: 120000, inspectTimeoutMs: 30000,
  maxOutputBytes: 256 * 1024, killGraceMs: 1000 });
export const outputFormat = Object.freeze({ newline: '\n', utf8: 'utf8' });
export const operatingSystem = Object.freeze({ linux: 'linux' });
export const message = Object.freeze({ invalidEnvironment: 'Required GitHub source context is invalid.',
  invalidWorkspace: 'GitHub workspace is invalid.', sourceMismatch: 'Checked-out source does not match GITHUB_SHA.',
  trackedChanges: 'Source changes are not allowed for delivered-source qualification.',
  unsupportedPlatform: 'Delivered-source qualification requires a Linux runner.' });
