export const envName = Object.freeze({
  githubSha: 'GITHUB_SHA',
  runId: 'GITHUB_RUN_ID',
  runAttempt: 'GITHUB_RUN_ATTEMPT',
  repository: 'GITHUB_REPOSITORY',
  ref: 'GITHUB_REF',
  runnerTemp: 'RUNNER_TEMP',
  workspace: 'GITHUB_WORKSPACE',
  githubOutput: 'GITHUB_OUTPUT',
  dockerHost: 'DOCKER_HOST',
});

export const fileName = Object.freeze({
  receipt: 'image-receipt.json',
  cleanupReceipt: 'registry-cleanup.json',
  serverManifest: 'server-manifest.json',
  comparisonsManifest: 'comparisons-manifest.json',
  registryLogs: 'registry.log',
  registryState: 'registry-state.json',
  nativeCommands: 'native-commands.jsonl',
  registryHeaders: 'registry-headers.jsonl',
  registryReadiness: 'registry-readiness.jsonl',
  serverDockerfile: 'Dockerfile',
  runnerDockerfile: 'benchmarks/KeyLoad.ComparisonHost/Features/BenchmarkComparisons/Dockerfile',
});

export const directoryName = Object.freeze({ evidence: 'keyload-images' });
export const registry = Object.freeze({
  image: 'registry:3.1.2@sha256:ddf754342cfc8acc51a56d5d0ab6af06826461864460636d8bd5c546dab2a7b8',
  host: '127.0.0.1',
  port: 5000,
  url: 'http://127.0.0.1:5000',
  containerPrefix: 'keyload-images-',
  pathPrefix: 'keyload',
  sourceLabel: 'org.opencontainers.image.revision',
  ownerRunLabel: 'io.keyload.image-run-id',
  ownerAttemptLabel: 'io.keyload.image-run-attempt',
  ownerRepositoryLabel: 'io.keyload.image-repository',
});
export const baseImage = Object.freeze({
  sdk: 'mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317',
  aspnet: 'mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4',
});

export const imageKind = Object.freeze({
  server: 'server',
  comparisons: 'comparisons',
  loadGenerator: 'load-generator',
});

export const imageBuild = Object.freeze({
  platform: 'linux/amd64',
  serverDockerfile: fileName.serverDockerfile,
  runnerDockerfile: fileName.runnerDockerfile,
  context: '.',
  repository: 'managedcode/KeyLoad',
});

export const buildArgument = Object.freeze({
  buildx: 'buildx',
  version: 'version',
  inspect: 'inspect',
  build: 'build',
  platform: '--platform',
  load: '--load',
  tag: '--tag',
  file: '--file',
  label: '--label',
  revisionLabel: `${registry.sourceLabel}=`,
  imageInspect: 'image',
  format: '--format',
  configLabelTemplate: '{{.Id}}|{{index .Config.Labels "org.opencontainers.image.revision"}}',
  push: 'push',
});

export const dockerArgument = Object.freeze({
  docker: 'docker',
  context: 'context',
  show: 'show',
  contextInspect: 'inspect',
  format: '--format',
  contextEndpointTemplate: '{{(index .Endpoints "docker").Host}}',
  version: '--version',
  info: 'info',
  operatingSystemTemplate: '{{.OSType}}',
  run: 'run',
  detach: '--detach',
  name: '--name',
  publish: '--publish',
  environment: '--env',
  label: '--label',
  logs: 'logs',
  tail: '--tail',
  inspect: 'inspect',
  inspectTemplate: '{{json .}}',
  remove: 'rm',
  force: '--force',
  image: 'image',
});

export const registryProtocol = Object.freeze({
  path: '/v2/',
  repositories: '/v2/keyload/',
  manifests: '/manifests/',
  acceptHeader: 'application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.v2+json',
  ociManifestMediaType: 'application/vnd.oci.image.manifest.v1+json',
  dockerManifestMediaType: 'application/vnd.docker.distribution.manifest.v2+json',
  accept: 'Accept',
  digestHeader: 'docker-content-digest',
  contentTypeHeader: 'content-type',
  contentLengthHeader: 'content-length',
  registryDeleteDisabled: 'REGISTRY_STORAGE_DELETE_ENABLED=false',
});

export const imageReference = Object.freeze({
  outputServer: 'server-image',
  outputLoadGenerator: 'load-generator-image',
  repositoryPrefix: '127.0.0.1:5000/keyload/',
  serverName: 'server',
  comparisonsName: 'comparisons',
  tagSeparator: ':',
  runSeparator: '-',
  digestSeparator: '@',
});

export const receiptField = Object.freeze({
  schemaVersion: 'schemaVersion',
  sourceSha: 'sourceSha',
  runId: 'runId',
  runAttempt: 'runAttempt',
  repository: 'repository',
  ref: 'ref',
  sourceRevision: 'sourceRevision',
  registry: 'registry',
  baseImages: 'baseImages',
  github: 'github',
  bases: 'bases',
  images: 'images',
  preparedAt: 'preparedAt',
  name: 'name',
  finalReference: 'finalReference',
  reference: 'reference',
  manifest: 'manifest',
  manifestBytesBase64: 'manifestBytesBase64',
  manifestSha256: 'manifestSha256',
  digestHeader: 'digestHeader',
  contentTypeHeader: 'contentTypeHeader',
  configImageId: 'configImageId',
  sourceLabel: 'sourceLabel',
  sourceLabelValue: 'sourceLabelValue',
  manifestDigest: 'manifestDigest',
  registryDigest: 'registryDigest',
  manifestFile: 'manifestFile',
  revisionLabel: 'revisionLabel',
  configId: 'configId',
  attempt: 'attempt',
  sdk: 'sdk',
  runtime: 'runtime',
  server: 'server',
  comparisons: 'comparisons',
  runtimeImage: 'runtimeImage',
  sdkImage: 'sdkImage',
  registryImage: 'registryImage',
  schema: 1,
  cleanup: 'cleanup',
  containerName: 'containerName',
  owned: 'owned',
  found: 'found',
  removed: 'removed',
  exitCode: 'exitCode',
  logBytes: 'logBytes',
  stateBytes: 'stateBytes',
  outputTruncated: 'outputTruncated',
  capturedAt: 'capturedAt',
  result: 'result',
  serverImage: 'server-image',
  loadGeneratorImage: 'load-generator-image',
});

export const validation = Object.freeze({
  shaPattern: /^[a-f0-9]{40}$/i,
  digestPattern: /^sha256:[a-f0-9]{64}$/i,
  configIdPattern: /^sha256:[a-f0-9]{64}$/i,
  containerIdPattern: /^[a-f0-9]{64}$/i,
  numericIdPattern: /^[1-9][0-9]{0,19}$/,
  repositoryPattern: /^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/,
  refPattern: /^refs\/[A-Za-z0-9_./-]+$/,
  maxRepositoryLength: 200,
  maxRefLength: 255,
  maxPathLength: 4096,
  controlCharacterPattern: /[\u0000-\u001f\u007f]/,
  imageReferencePattern: /^127\.0\.0\.1:5000\/keyload\/(server|comparisons):[a-f0-9]{40}-[1-9][0-9]{0,19}-[1-9][0-9]{0,19}@sha256:[a-f0-9]{64}$/i,
});

export const processLimit = Object.freeze({
  commandTimeoutMs: 120000,
  buildTimeoutMs: 900000,
  pushTimeoutMs: 120000,
  inspectTimeoutMs: 30000,
  cleanupTimeoutMs: 30000,
  readinessTimeoutMs: 30000,
  readinessProbeTimeoutMs: 2000,
  readinessIntervalMs: 250,
  maxRegistryReadinessRecords: 121,
  maxRegistryReadinessBytes: 64 * 1024,
  maxOutputBytes: 256 * 1024,
  maxLogBytes: 128 * 1024,
  maxInspectBytes: 64 * 1024,
  maxCommandEvidenceBytes: 8 * 1024 * 1024,
  maxCommandEvidenceRecordBytes: 512 * 1024,
  maxNativeCommandRecords: 64,
  maxManifestBytes: 4 * 1024 * 1024,
  maxLogLines: 200,
  killGraceMs: 1000,
});

export const registryProbeErrorCodes = Object.freeze([
  'ECONNREFUSED', 'ECONNRESET', 'EPIPE', 'ETIMEDOUT', 'EHOSTUNREACH', 'ENETUNREACH',
  'UND_ERR_CONNECT_TIMEOUT', 'UND_ERR_HEADERS_TIMEOUT', 'UND_ERR_BODY_TIMEOUT', 'UND_ERR_SOCKET',
]);

export const registryReadinessTokens = Object.freeze({
  phase: Object.freeze({ request: 'request', bodyCancel: 'body-cancel' }),
  outcome: Object.freeze({ ready: 'ready', httpStatus: 'http-status', timeout: 'timeout',
    requestFailed: 'request-failed', bodyCancelFailed: 'body-cancel-failed' }),
});

export const outputFormat = Object.freeze({
  jsonIndent: 2,
  newline: '\n',
  utf8: 'utf8',
  base64: 'base64',
  sha256: 'sha256',
  hex: 'hex',
});

export const operatingSystem = Object.freeze({ linux: 'linux', unixPrefix: 'unix:///', });

export const message = Object.freeze({
  invalidArguments: 'Image tooling accepts no command-line options.',
  invalidEnvironment: 'Required GitHub image preparation context is invalid.',
  invalidWorkspace: 'GitHub workspace is invalid.',
  sourceMismatch: 'Checked-out source does not match GITHUB_SHA.',
  trackedChanges: 'Tracked source changes are not allowed for image preparation.',
  unsupportedPlatform: 'Image preparation requires a Linux runner.',
  remoteEngine: 'Image preparation requires the local Unix Docker Engine.',
  missingDocker: 'Docker CLI or local Engine is unavailable.',
  missingBuildx: 'Docker Buildx is unavailable.',
  commandFailed: 'A required image preparation command failed.',
  commandTimeout: 'A required image preparation command exceeded its time bound.',
  commandOutputLimit: 'A required image preparation command exceeded its output bound.',
  invalidOutput: 'Docker returned invalid bounded image metadata.',
  invalidManifest: 'The local registry returned invalid image manifest evidence.',
  registryTimeout: 'The owned local registry did not become ready in time.',
  unsafeEvidencePath: 'The owned image evidence path is unsafe.',
  evidenceExists: 'Image evidence already exists for this job attempt.',
  missingOwnedRegistry: 'The unique job-owned registry container was not found.',
  foreignRegistry: 'The registry container does not match this job attempt.',
  cleanupFailed: 'Owned registry cleanup did not complete successfully.',
  fetchFailed: 'The local registry manifest request failed.',
});

export function safeErrorMessage(error, fallback) {
  return error instanceof Error && Object.values(message).includes(error.message) ? error.message : fallback;
}

export const exitCode = Object.freeze({ success: 0, failure: 1, notFound: 1 });
