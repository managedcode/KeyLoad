import path from 'node:path';
import { exactSession as exact, requireSession as require, sessionHash, sessionPositive, sessionText } from './session-contract.mjs';

const binaryFields = ['executablePath', 'executableSha256', 'assemblyPath', 'assemblySha256', 'pdbPath',
  'pdbSha256', 'sourceCount', 'sourceCompositionSha256'];
const processFields = ['name', 'processId', 'startedAtUtc', 'aspireStartedAtUtc', 'binary'];
const absolute = value => sessionText(value) && path.isAbsolute(value) && path.normalize(value) === value;
const utc = value => typeof value === 'string' && /Z$/u.test(value) && Number.isFinite(Date.parse(value)) && Date.parse(value) > 0;

export function validateNativeProcess(value, role, expectedName, runtimeIdentity = null) {
  require(exact(value, processFields) && value.name === expectedName && sessionPositive(value.processId)
    && value.processId <= 2_147_483_647 && utc(value.startedAtUtc)
    && (value.aspireStartedAtUtc === null ? role === 'comparison-client' : utc(value.aspireStartedAtUtc))
    && exact(value.binary, binaryFields));
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

export function validateSessionProcesses(value, row, runtimeIdentity = null) {
  require(exact(value, ['client', 'servers']) && Array.isArray(value.servers)
    && value.servers.length === (row.target === 'KeyLoad' ? row.nodeCount : 0));
  validateNativeProcess(value.client, 'comparison-client', 'comparison-client', runtimeIdentity);
  const pids = new Set([value.client.processId]);
  for (const [index, server] of value.servers.entries()) {
    validateNativeProcess(server, 'server', 'node' + (index + 1), runtimeIdentity);
    require(server.aspireStartedAtUtc !== null && !pids.has(server.processId)
      && Date.parse(server.startedAtUtc) <= Date.parse(value.client.startedAtUtc));
    pids.add(server.processId);
  }
  return value;
}
