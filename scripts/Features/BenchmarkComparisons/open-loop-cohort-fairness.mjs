import { isDeepStrictEqual } from 'node:util';
import { reject, CELL_TERMINAL_ERRORS } from './open-loop-cell-terminal-contract.mjs';

export function createOpenLoopFairnessValidator() {
  const groups = new Map();
  return item => retainComparableFacts(groups, item);
}

function retainComparableFacts(groups, item) {
  if (item.disposition !== 'measured') return;
  const { report, sidecar } = item;
  const key = JSON.stringify([item.cell.profile, item.cell.nodeCount, item.cell.scenario,
    item.cell.offeredRatePerSecond]);
  const facts = { datasetRecords: report.datasetRecords, datasetSha256: report.datasetSha256,
    executionPolicy: report.executionPolicy, acknowledgement: report.target.writeAcknowledgement,
    readContract: report.target.readContract, authorization: report.target.authorization,
    dataCopies: report.target.cluster.dataCopies, hardware: sidecar.hardware,
    appHostEnvelope: sidecar.appHostEnvelope, containers: comparableContainers(sidecar.containers) };
  const previous = groups.get(key);
  reject(previous === undefined || isDeepStrictEqual(previous, facts), CELL_TERMINAL_ERRORS.cohort);
  groups.set(key, facts);
}

function comparableContainers(containers) {
  return containers.map(item => ({ effectiveCpuQuota: item.effectiveCpuQuota,
    effectiveCpuSet: item.effectiveCpuSet, effectiveMemoryLimitBytes: item.effectiveMemoryLimitBytes,
    writableMounts: item.writableMounts.map(mount => ({ fileSystem: mount.fileSystem,
      capacityBytes: mount.capacityBytes })).sort(compareJson) })).sort(compareJson);
}

function compareJson(left, right) {
  const first = JSON.stringify(left);
  const second = JSON.stringify(right);
  return first < second ? -1 : first > second ? 1 : 0;
}
