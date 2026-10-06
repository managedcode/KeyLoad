import path from 'node:path';

export const format = Object.freeze({
  invocationOption: '--invocation=',
  descriptorLimitOption: '--maximum-descriptor-bytes=',
  maximumDescriptorEnvelopeBytes: 1_048_576,
  maximumDecimalDigits: 7,
  maximumPathCharacters: 4096,
  schemaVersion: 1,
  packageId: 'dotnet-coverage',
  dependencyManifestName: 'dotnet-coverage.deps.json',
  runtimeConfigName: 'dotnet-coverage.runtimeconfig.json',
  packageToolDirectory: 'tools/net8.0/any',
  linuxNativeDirectory: 'ubuntu/x64',
  linuxNativeFiles: Object.freeze([
    'libInstrumentationEngine.so',
    'libCoverageInstrumentationMethod.so',
    'Cov_x64.config',
  ]),
  outputManifestPath: 'identity/context-manifest.json',
  outputRuntimeIdentityPath: 'identity/runtime-identity.env',
  serverDllName: 'KeyLoad.Server.dll',
  serverPdbName: 'KeyLoad.Server.pdb',
  settingsName: 'settings.xml',
  wrapperName: 'server-wrapper.sh',
  lifecycleName: 'server-lifecycle.sh',
  targetName: 'server-target.sh',
  dockerfileName: 'Dockerfile',
  dockerignoreName: '.dockerignore',
  sourceReceiptName: 'identity/server-source-receipt.json',
  baseReceiptName: 'identity/base-image-receipt.json',
  manifestFileName: 'context-manifest.json',
  toolDirectoryName: 'tool',
  serverDirectoryName: 'server',
  licenseDirectoryName: 'license',
  licenseFiles: Object.freeze(['License.txt', 'ThirdPartyNotices.txt']),
});

export function fail(message) {
  throw new Error(message);
}

export function hasExactKeys(value, keys) {
  return isRecord(value) && Object.keys(value).sort().join('\n') === [...keys].sort().join('\n');
}

export function isRecord(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

export function isPositiveInteger(value) {
  return Number.isSafeInteger(value) && value > 0;
}

export function assertHex(value, length, message) {
  if (typeof value !== 'string' || !new RegExp(`^[0-9a-f]{${length}}$`).test(value)) {
    fail(message);
  }
}

export function assertAbsolutePath(value, maximumCharacters, message) {
  if (typeof value !== 'string' || value.length === 0 || value.length > maximumCharacters ||
      !path.isAbsolute(value) || value.includes('\0') || value.includes('\n') || value.includes('\r')) {
    fail(message);
  }
  return path.normalize(value);
}

export function validateInvocation(value) {
  const topKeys = [
    'schemaVersion', 'invocationId', 'contextDirectory', 'serverPublishDirectory', 'server',
    'baseImage', 'tool', 'bounds',
  ];
  if (!hasExactKeys(value, topKeys) || value.schemaVersion !== format.schemaVersion) {
    fail('The image invocation descriptor is invalid.');
  }
  validateBounds(value.bounds);
  if (typeof value.invocationId !== 'string' ||
      !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(value.invocationId)) {
    fail('The image invocation identifier is invalid.');
  }
  const maximumPath = Math.min(value.bounds.maximumPathCharacters, format.maximumPathCharacters);
  value.contextDirectory = assertAbsolutePath(value.contextDirectory, maximumPath,
    'The output context path is invalid.');
  value.serverPublishDirectory = assertAbsolutePath(value.serverPublishDirectory, maximumPath,
    'The server publish path is invalid.');
  validateServerIdentity(value.server, maximumPath);
  validateBaseIdentity(value.baseImage, maximumPath);
  validateToolIdentity(value.tool, maximumPath);
  assertOutputDoesNotOverlapInputs(value);
  return value;
}

function assertOutputDoesNotOverlapInputs(invocation) {
  const output = invocation.contextDirectory;
  const inputs = [invocation.serverPublishDirectory, invocation.server.sourceReceiptPath,
    invocation.baseImage.sourceReceiptPath, invocation.tool.packageRoot];
  for (const input of inputs) {
    if (pathsOverlap(output, input)) {
      fail('The output context path overlaps an admitted input path.');
    }
  }
}

function pathsOverlap(left, right) {
  return pathIsWithin(left, right) || pathIsWithin(right, left);
}

function pathIsWithin(parent, candidate) {
  const relative = path.relative(parent, candidate);
  return relative === '' || (relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative));
}

function validateBounds(bounds) {
  const keys = [
    'maximumFiles', 'maximumTotalBytes', 'maximumFileBytes', 'maximumPathCharacters', 'maximumManifestBytes',
    'readBufferBytes', 'shutdownSeconds', 'settlementSeconds', 'maximumReportBytes',
  ];
  if (!hasExactKeys(bounds, keys) || Object.values(bounds).some((entry) => !isPositiveInteger(entry)) ||
      bounds.maximumPathCharacters > format.maximumPathCharacters ||
      bounds.readBufferBytes > format.maximumDescriptorEnvelopeBytes ||
      bounds.readBufferBytes > bounds.maximumFileBytes ||
      bounds.maximumManifestBytes > bounds.maximumFileBytes ||
      bounds.maximumManifestBytes > bounds.maximumTotalBytes || bounds.maximumFiles < 2) {
    fail('Image invocation bounds are invalid.');
  }
}

function validateServerIdentity(server, maximumPath) {
  if (!hasExactKeys(server, ['dllSha256', 'pdbSha256', 'mvid', 'sourceReceiptPath', 'sourceReceiptSha256'])) {
    fail('The server identity descriptor is invalid.');
  }
  assertHex(server.dllSha256, 64, 'The server DLL identity is invalid.');
  assertHex(server.pdbSha256, 64, 'The server PDB identity is invalid.');
  assertHex(server.sourceReceiptSha256, 64, 'The server source receipt identity is invalid.');
  if (typeof server.mvid !== 'string' ||
      !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(server.mvid)) {
    fail('The server module identity is invalid.');
  }
  server.sourceReceiptPath = assertAbsolutePath(server.sourceReceiptPath, maximumPath,
    'The server source receipt path is invalid.');
}

function validateBaseIdentity(baseImage, maximumPath) {
  if (!hasExactKeys(baseImage, ['reference', 'sourceReceiptPath', 'sourceReceiptSha256'])) {
    fail('The source-bound ASP.NET base image descriptor is invalid.');
  }
  baseImage.sourceReceiptPath = assertAbsolutePath(baseImage.sourceReceiptPath, maximumPath,
    'The base image receipt path is invalid.');
  assertHex(baseImage.sourceReceiptSha256, 64, 'The base image receipt identity is invalid.');
  if (typeof baseImage.reference !== 'string' ||
      !/^mcr\.microsoft\.com\/dotnet\/aspnet:[A-Za-z0-9._-]+@sha256:[0-9a-f]{64}$/.test(baseImage.reference)) {
    fail('The ASP.NET image must be a digest reference from the source-bound receipt.');
  }
}

function validateToolIdentity(tool, maximumPath) {
  if (!hasExactKeys(tool, ['version', 'packageRoot']) ||
      typeof tool.version !== 'string' || !/^\d+\.\d+\.\d+$/.test(tool.version)) {
    fail('The native coverage tool descriptor is invalid.');
  }
  tool.packageRoot = assertAbsolutePath(tool.packageRoot, maximumPath,
    'The native coverage package path is invalid.');
}

export function parseArguments(args) {
  if (args.length !== 2 || !args[0].startsWith(format.invocationOption) ||
      !args[1].startsWith(format.descriptorLimitOption)) {
    fail('Usage: node functional-coverage.server-image.mjs --invocation=<file> --maximum-descriptor-bytes=<bytes>');
  }
  const invocationPath = args[0].slice(format.invocationOption.length);
  const decimal = args[1].slice(format.descriptorLimitOption.length);
  if (!/^[1-9][0-9]{0,6}$/.test(decimal) || decimal.length > format.maximumDecimalDigits) {
    fail('The descriptor byte limit is invalid.');
  }
  const maximumBytes = Number(decimal);
  if (maximumBytes > format.maximumDescriptorEnvelopeBytes) {
    fail('The descriptor byte limit exceeds the immutable protocol envelope.');
  }
  return {
    invocationPath: assertAbsolutePath(invocationPath, format.maximumPathCharacters,
      'The invocation path is invalid.'),
    maximumBytes,
  };
}
