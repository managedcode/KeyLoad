import path from 'node:path';
import { exactSession as exact, requireSession as require, sessionHash, sessionPositive, sessionText } from './session-contract.mjs';

const binaryFields = ['executablePath', 'executableSha256', 'assemblyPath', 'assemblySha256', 'pdbPath',
  'pdbSha256', 'sourceCount', 'sourceCompositionSha256'];
const processFields = ['name', 'processId', 'startedAtUtc', 'aspireStartedAtUtc',
  'aspireProcessId', 'aspireProcessStartedAtUtc', 'binary'];
const absolute = value => sessionText(value) && path.isAbsolute(value) && path.normalize(value) === value;
const utc = value => typeof value === 'string' && /Z$/u.test(value) && Number.isFinite(Date.parse(value)) && Date.parse(value) > 0;

function validateProcess(value, role, expectedName, runtimeIdentity = null) {
  require(exact(value, processFields) && value.name === expectedName && sessionPositive(value.processId)
    && value.processId <= 2_147_483_647 && utc(value.startedAtUtc)
    && exact(value.binary, binaryFields));
  require(role === 'comparison-client' && value.aspireStartedAtUtc === null && value.aspireProcessId === null
    && value.aspireProcessStartedAtUtc === null);
  const binary = value.binary;
  require([binary.executablePath, binary.assemblyPath, binary.pdbPath].every(absolute)
    && [binary.executableSha256, binary.assemblySha256, binary.pdbSha256, binary.sourceCompositionSha256].every(sessionHash)
    && sessionPositive(binary.sourceCount) && binary.sourceCount <= 5_000
    && binary.pdbPath === binary.assemblyPath.slice(0, -4) + '.pdb' && binary.assemblyPath.endsWith('.dll'));
  if (runtimeIdentity !== null) {
    const expected = runtimeIdentity.descriptor.assemblies.find(item => item.role === role);
    require(expected !== undefined && binary.assemblyPath === path.join(runtimeIdentity.descriptor.workspace, expected.path)
      && binary.pdbPath === path.join(runtimeIdentity.descriptor.workspace, expected.pdbPath)
      && binary.assemblySha256 === expected.sha256 && binary.pdbSha256 === expected.pdbSha256);
  }
  return value;
}

// Every database server, KeyLoad included, is a digest-pinned container identified by its image, not a host process.
export function validateSessionProcesses(value, runtimeIdentity = null) {
  require(exact(value, ['client']));
  validateProcess(value.client, 'comparison-client', 'comparison-client', runtimeIdentity);
  return value;
}
