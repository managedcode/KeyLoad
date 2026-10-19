import path from 'node:path';

export const format = Object.freeze({
  packageId: 'dotnet-coverage',
  dependencyManifestName: 'dotnet-coverage.deps.json',
  runtimeConfigName: 'dotnet-coverage.runtimeconfig.json',
  packageToolDirectory: 'tools/net8.0/any',
  linuxDirectory: 'ubuntu/x64',
  linuxFiles: Object.freeze(['libInstrumentationEngine.so', 'libCoverageInstrumentationMethod.so', 'Cov_x64.config']),
  licenseFiles: Object.freeze(['License.txt', 'ThirdPartyNotices.txt']),
});

export function fail(message) {
  throw new Error(message);
}

export function assertAbsolutePath(value, maximumCharacters, message) {
  if (typeof value !== 'string' || value.length === 0 || value.length > maximumCharacters ||
      !path.isAbsolute(value) || value.includes('\0') || value.includes('\n') || value.includes('\r')) {
    fail(message);
  }
  return path.normalize(value);
}

