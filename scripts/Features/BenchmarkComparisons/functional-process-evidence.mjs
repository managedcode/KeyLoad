import { isDeepStrictEqual } from 'node:util';
import { exactSession as exact, requireSession as require, sessionText } from './session-contract.mjs';
import { validateProcess } from './session-process-identity.mjs';

const processFields = ['Name', 'ProcessId', 'StartedAtUtc', 'AspireStartedAtUtc',
  'AspireProcessId', 'AspireProcessStartedAtUtc', 'Binary'];
const binaryFields = ['ExecutablePath', 'ExecutableSha256', 'AssemblyPath', 'AssemblySha256',
  'PdbPath', 'PdbSha256', 'SourceCount', 'SourceCompositionSha256'];
const resourceFields = ['Name', 'ProcessId', 'StartedAtUtc', 'AspireStartedAtUtc',
  'AspireProcessId', 'AspireProcessStartedAtUtc', 'ExitCode',
  'StartTimeStamp', 'StopTimeStamp', 'State'];
const stamp = value => typeof value === 'string' && Number.isFinite(Date.parse(value)) && Date.parse(value) > 0;

function runtimeProcesses(value, identity) {
  require(Array.isArray(value) && value.length === 3);
  const pids = new Set();
  return value.map((original, index) => {
    require(exact(original, processFields) && exact(original.Binary, binaryFields));
    const binary = original.Binary;
    const observed = validateProcess({ name: original.Name, processId: original.ProcessId,
      startedAtUtc: original.StartedAtUtc, aspireStartedAtUtc: original.AspireStartedAtUtc,
      aspireProcessId: original.AspireProcessId, aspireProcessStartedAtUtc: original.AspireProcessStartedAtUtc,
      binary: { executablePath: binary.ExecutablePath, executableSha256: binary.ExecutableSha256,
        assemblyPath: binary.AssemblyPath, assemblySha256: binary.AssemblySha256, pdbPath: binary.PdbPath,
        pdbSha256: binary.PdbSha256, sourceCount: binary.SourceCount,
        sourceCompositionSha256: binary.SourceCompositionSha256 } }, 'server', 'node' + (index + 1), identity);
    require(!pids.has(observed.processId) && !pids.has(observed.aspireProcessId)
      && observed.aspireStartedAtUtc !== null);
    pids.add(observed.processId);
    pids.add(observed.aspireProcessId);
    return observed;
  });
}

// Integration emits these exact three originals using its owning default Pascal-case serializer.
export function validateFunctionalProcesses(startup, beforeShutdown, shutdown, identity) {
  const processes = runtimeProcesses(startup, identity);
  require(isDeepStrictEqual(startup, beforeShutdown) && exact(shutdown, ['JoinedAtUtc', 'Processes', 'Resources'])
    && stamp(shutdown.JoinedAtUtc) && isDeepStrictEqual(shutdown.Processes, beforeShutdown)
    && Array.isArray(shutdown.Resources) && shutdown.Resources.length === processes.length);
  for (const [index, original] of beforeShutdown.entries()) {
    const actual = shutdown.Resources[index];
    require(exact(actual, resourceFields) && processFields.slice(0, -1).every(key => actual[key] === original[key])
      && actual.ExitCode === 0 && sessionText(actual.State) && actual.State !== 'Running'
      && stamp(actual.StartTimeStamp) && stamp(actual.StopTimeStamp)
      && Date.parse(actual.StartTimeStamp) === Date.parse(original.AspireStartedAtUtc)
      && Date.parse(actual.StopTimeStamp) >= Date.parse(actual.StartTimeStamp)
      && Date.parse(shutdown.JoinedAtUtc) >= Date.parse(actual.StopTimeStamp));
  }
  return processes;
}
