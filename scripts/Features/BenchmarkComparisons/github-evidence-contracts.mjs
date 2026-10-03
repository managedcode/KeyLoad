import { createHash } from 'node:crypto';
import { readFile, stat } from 'node:fs/promises';

export const C = Object.freeze({
  repo: 'managedcode/KeyLoad', repoWebBase: 'https://github.com/managedcode/KeyLoad', workflowPath: '.github/workflows/ci.yml', workflowName: 'KeyLoad CI', workflowDisplayName: 'CI',
  main: 'main', push: 'push', active: 'active', completed: 'completed', success: 'success',
  files: ['workflow.json', 'runs-pages.json', 'run.json', 'jobs-pages.json', 'artifacts-pages.json'],
  attempts: 'attempts', run: 'run.json', jobs: 'jobs-pages.json', maxRuns: 1000, maxPairs: 10000,
  attemptFilesPerPair: 2,
  maxMetadataBytes: 16_777_216, maxArchiveBytes: 134_217_728,
  digestPrefix: 'sha256:', modeValidate: 'validate', modePublish: 'publish',
  stateSelected: 'selected', stateUnavailable: 'unavailable', stateNeedsAttempt: 'needs_attempt',
  stateMetadata: 'metadata_verified', stateArchive: 'archive_verified', noComparison: 'no_successful_comparison',
  cmdSelect: 'select', cmdProve: 'prove', cmdArchive: 'verify-archive', cmdFresh: 'fresh',
  errorArgument: 'E_ARGUMENT', errorCapture: 'E_CAPTURE', errorPagination: 'E_PAGINATION',
  errorWorkflow: 'E_WORKFLOW', errorRun: 'E_RUN', errorJob: 'E_JOB', errorArtifact: 'E_ARTIFACT',
  errorArchive: 'E_ARCHIVE', errorSource: 'E_SOURCE', errorFreshness: 'E_FRESHNESS',
  repoId: 477801965, artifactName: 'comparison-suite', jobName: 'comparison-smoke',
  steps: [
    'Run dotnet test --project tests/KeyLoad.ComparisonTests --no-build --no-restore --configuration Release',
    'Measure 1 KiB documents, eight clients and three graph hops',
    'Measure 16 KiB documents, four clients and five graph hops',
  ],
  fileWorkflow: 'workflow.json', fileRuns: 'runs-pages.json', fileRun: 'run.json', fileJobs: 'jobs-pages.json',
  fileArtifacts: 'artifacts-pages.json',
  hashAlgorithm: 'sha256', textEncoding: 'utf8', hexEncoding: 'hex', oneCharacter: 1,
  valueTypes: Object.freeze({ object: 'object', string: 'string' }),
  filesystemErrors: Object.freeze({ missingEntry: 'ENOENT', notDirectory: 'ENOTDIR' }),
  shaPattern: /^[0-9a-f]{40}$/,
  digestPattern: /^sha256:[0-9a-f]{64}$/,
  metadataShaPattern: /^[0-9a-f]{64}$/,
  attemptMetadataPattern: /^attempts\/([1-9][0-9]*)\/([1-9][0-9]*)\/(run\.json|jobs-pages\.json)$/,
  runIdentifierPattern: /^[1-9][0-9]*$/,
  messages: Object.freeze({
    metadataTooLarge: 'Evidence metadata file is missing or exceeds its size bound.',
    metadataInvalid: 'Evidence metadata file is missing or invalid JSON.',
    fileTooLarge: 'Evidence file is missing or exceeds its size bound.',
    fileUnreadable: 'Evidence file is missing or unreadable.',
    runPaginationEmpty: 'Run pagination is empty or malformed.',
    runPaginationPageInvalid: 'Run pagination page is malformed.',
    runPaginationCountsDisagree: 'Run pagination counts disagree.',
    runPaginationIdentityInvalid: 'Run pagination contains an invalid or duplicate run identity.',
    paginationIdentityInvalid: 'Paginated GitHub response contains an invalid or duplicate identity.',
    runPaginationIncomplete: 'Run pagination is incomplete or exceeds its bound.',
    workflowInvalid: 'Captured workflow is not the active canonical CI workflow.',
    runInvalid: 'Captured run identity, source, or repository is not eligible.',
    selectionArgumentsInvalid: 'Evidence selection arguments are invalid.',
    requestedRunInvalid: 'Requested run is absent or ambiguous.',
    attemptHistoryTooLarge: 'Attempt history exceeds its bound.',
    attemptPairRequired: 'Attempt run and jobs captures must be retained as a pair.',
    attemptRunMismatch: 'Attempt response does not match its run summary.',
    attemptJobsInvalid: 'Attempt jobs capture is malformed.',
    comparisonJobAmbiguous: 'Attempt has ambiguous comparison jobs.',
    comparisonJobIdentityMismatch: 'Comparison job identity does not match its selected attempt.',
    comparisonJobIncomplete: 'Successful comparison job is incomplete.',
    comparisonStepMissing: 'Successful comparison job lacks one exact successful measurement step.',
    comparisonStepsAmbiguous: 'Successful comparison job steps are ambiguous.',
    paginationInvalid: 'Paginated GitHub response is malformed.',
    paginationPageInvalid: 'Paginated GitHub response page is malformed.',
    paginationCountsDisagree: 'Paginated GitHub response counts disagree.',
    paginationIncomplete: 'Paginated GitHub response is incomplete.',
    attemptCannotInspect: 'Attempt capture cannot be inspected.',
    sourceRevisionsInvalid: 'Site and control source revisions must be full commit SHAs.',
    selectionNotSuccessful: 'Evidence selection did not identify a successful comparison.',
    attemptedJobUnsuccessful: 'Selected attempt no longer contains a successful comparison job.',
    metadataReceiptInvalid: 'Metadata receipt is not eligible for archive verification.',
    archiveMismatch: 'Retained archive bytes do not match authenticated artifact metadata.',
    freshnessMismatch: 'Fresh publication evidence no longer matches the archive-qualified measurement.',
    artifactNotUnique: 'Selected run must contain exactly one comparison-suite artifact.',
    artifactInvalid: 'Comparison artifact is expired, oversized, mistimed, or bound to another run.',
    artifactTimestampInvalid: 'Artifact timestamp is invalid.',
    jobStartTimestampInvalid: 'Comparison job start timestamp is invalid.',
    jobCompletionTimestampInvalid: 'Comparison job completion timestamp is invalid.',
    topComparisonJobNotUnique: 'Top-level exact-attempt jobs do not contain one selected comparison job.',
    topComparisonJobMismatch: 'Top-level selected comparison job differs from the authenticated attempt.',
    topComparisonStepsInvalid: 'Selected job does not have the exact successful measurement steps.',
    topRunMismatch: 'Top-level selected run differs from the authenticated attempt response.',
    topJobMismatch: 'Top-level selected job differs from the exact-attempt jobs response.',
    unsupportedCommand: 'A supported evidence command is required.',
    invalidArguments: 'Use unique known --name=value arguments.',
    evidenceValidationFailed: 'Evidence validation failed.',
    requiredArgumentPrefix: 'Required argument --',
    requiredArgumentSuffix: '= is missing.',
    absoluteArgumentPrefix: 'Argument --',
    absoluteArgumentSuffix: '= must be an absolute path.',
  }),
});

export const F = Object.freeze({
  id: 'id', name: 'name', path: 'path', state: 'state', event: 'event', headBranch: 'head_branch', headSha: 'head_sha',
  workflowId: 'workflow_id', runNumber: 'run_number', runAttempt: 'run_attempt', repository: 'repository',
  fullName: 'full_name', headRepository: 'head_repository', workflowRuns: 'workflow_runs', totalCount: 'total_count',
  jobs: 'jobs', steps: 'steps', runId: 'run_id', htmlUrl: 'html_url', status: 'status', conclusion: 'conclusion',
  number: 'number', startedAt: 'started_at', completedAt: 'completed_at', artifacts: 'artifacts', expired: 'expired',
  sizeInBytes: 'size_in_bytes', digest: 'digest', createdAt: 'created_at', workflowRun: 'workflow_run',
  repositoryId: 'repository_id', headRepositoryId: 'head_repository_id', schemaVersion: 'schemaVersion',
  publishEligible: 'publishEligible', runKey: 'run', runIdKey: 'runId', runNumberKey: 'runNumber',
  runAttemptKey: 'runAttempt', attempt: 'attempt', measuredRevision: 'measuredSourceRevision', jobKey: 'comparisonJob', mode: 'mode',
  comparisonJobId: 'comparisonJobId', artifactKey: 'artifact', metadataFiles: 'metadataFiles',
  siteRevision: 'siteSourceRevision', controlRevision: 'controlWorkflowRevision', fullNameKey: 'fullName',
  workflowKey: 'workflow', url: 'url', reason: 'reason', sha256: 'sha256', bytes: 'bytes',
  fresh: 'fresh', ok: 'ok', result: 'result', error: 'error', errorCode: 'code', message: 'message',
  sizeBytes: 'sizeBytes', comparisonSteps: 'steps', artifactName: 'name', artifactCreatedAt: 'createdAt',
  artifactDigest: 'digest', jobStartedAt: 'startedAt', jobCompletedAt: 'completedAt', jobId: 'id',
  sha: 'sha256', archiveKey: 'archive',
});

export const A = Object.freeze({
  input: 'input', mode: 'mode', requestedRun: 'requested-run', siteRevision: 'site-revision',
  workflowRevision: 'workflow-revision', receipt: 'receipt', archive: 'archive', before: 'before', after: 'after',
  prefix: '--', separator: '=',
});

export class EvidenceError extends Error {
  constructor(code, message) { super(message); this.code = code; }
}

export const fail = (code, message) => { throw new EvidenceError(code, message); };
export const object = value => value !== null && typeof value === C.valueTypes.object && !Array.isArray(value);
export const positiveInteger = value => Number.isSafeInteger(value) && value > 0;
export const nonnegativeInteger = value => Number.isSafeInteger(value) && value >= 0;
export const validSha = value => typeof value === C.valueTypes.string && C.shaPattern.test(value);
export const validDigest = value => typeof value === C.valueTypes.string && C.digestPattern.test(value);
export const same = (left, right) => JSON.stringify(left) === JSON.stringify(right);

export async function readJson(file, code = C.errorCapture) {
  try {
    const info = await stat(file);
    if (!info.isFile() || info.size > C.maxMetadataBytes) fail(code, C.messages.metadataTooLarge);
    return JSON.parse(await readFile(file, C.textEncoding));
  } catch (error) {
    if (error instanceof EvidenceError) throw error;
    fail(code, C.messages.metadataInvalid);
  }
}

export async function digestFile(file, maxBytes = C.maxMetadataBytes, errorCode = C.errorArchive) {
  try {
    const info = await stat(file);
    if (!info.isFile() || info.size > maxBytes) fail(errorCode, C.messages.fileTooLarge);
    const bytes = await readFile(file);
    return { [F.bytes]: bytes.length, [F.sha256]: createHash(C.hashAlgorithm).update(bytes).digest(C.hexEncoding) };
  } catch (error) {
    if (error instanceof EvidenceError) throw error;
    fail(errorCode, C.messages.fileUnreadable);
  }
}

export function parseTime(value, code, message) {
  if (typeof value !== C.valueTypes.string) fail(code, message);
  const parsed = Date.parse(value);
  if (!Number.isFinite(parsed)) fail(code, message);
  return parsed;
}
