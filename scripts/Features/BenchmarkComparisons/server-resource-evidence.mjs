import { isDeepStrictEqual } from 'node:util';
import { hashPattern, requireGitHub } from './isolated-github-contract.mjs';

const schema = 'server-resource-evidence.v1';
const maximumContainers = 3;
const maximumSamples = 1680;
const maximumMounts = 8;
const missingKinds = Object.freeze(['effectiveServerResources', 'hardwareClass', 'serverCpuRss', 'storageEnvelope']);
const rootFields = Object.freeze(['schema', 'sourceRevision', 'workflowRunId', 'runAttempt', 'jobId', 'target',
  'nodeCount', 'scenario', 'profile', 'workerSha256', 'hardware', 'appHostEnvelope', 'containers', 'missingEvidence', 'qualified']);
const hardwareFields = Object.freeze(['kernel', 'architecture', 'cpuVendor', 'cpuFamily', 'cpuModel', 'cpuStepping',
  'logicalCpuCount', 'physicalCoreCount', 'logicalCpuMembership', 'physicalCoreMembership', 'memoryBytes']);
const envelopeFields = Object.freeze(['cpuQuota', 'cpuSet', 'memoryLimitBytes']);
const containerFields = Object.freeze(['resource', 'containerId', 'imageId', 'startedAt', 'state', 'cpuLimit',
  'memoryLimitBytes', 'effectiveCpuQuota', 'effectiveCpuSet', 'effectiveMemoryLimitBytes', 'observedCpuUsageUsec',
  'maxObservedCgroupMemoryBytes', 'maxObservedRssBytes', 'sampleCount', 'writableMounts']);
const mountFields = Object.freeze(['containerPath', 'mountType', 'fileSystem', 'capacityBytes', 'availableBytes']);

function keys(value, expected) {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    && isDeepStrictEqual(Object.keys(value).sort(), [...expected].sort());
}

function validHardware(value) {
  const logicalIds = keys(value, hardwareFields) ? parseOrderedIds(value.logicalCpuMembership) : null;
  return keys(value, hardwareFields) && [value.kernel, value.architecture, value.cpuVendor]
      .every(item => typeof item === 'string' && item.length > 0)
    && Number.isSafeInteger(value.cpuFamily) && value.cpuFamily > 0
    && Number.isSafeInteger(value.cpuModel) && value.cpuModel >= 0
    && Number.isSafeInteger(value.cpuStepping) && value.cpuStepping >= 0
    && [value.logicalCpuCount, value.physicalCoreCount, value.memoryBytes]
      .every(item => Number.isSafeInteger(item) && item > 0)
    && value.physicalCoreCount <= value.logicalCpuCount
    && /^\d+(?:,\d+)*$/u.test(value.logicalCpuMembership)
    && Array.isArray(value.physicalCoreMembership) && value.physicalCoreMembership.length === value.physicalCoreCount
    && value.physicalCoreMembership.every((item, index, all) => validCorePair(item)
      && (index === 0 || all[index - 1] < item))
    && logicalIds !== null && logicalIds.length === value.logicalCpuCount
    && new Set(value.physicalCoreMembership).size === value.physicalCoreCount;
}

function validCorePair(value) {
  if (typeof value !== 'string' || !/^(?:0|[1-9]\d*):(?:0|[1-9]\d*)$/u.test(value)) return false;
  const [socket, core] = value.split(':').map(Number);
  return Number.isSafeInteger(socket) && Number.isSafeInteger(core);
}

function validEnvelope(value, hardware) {
  const ranges = keys(value, envelopeFields) ? parseCpuSet(value.cpuSet) : null;
  const logicalIds = hardware === null ? null : parseOrderedIds(hardware.logicalCpuMembership);
  return keys(value, envelopeFields) && validQuota(value.cpuQuota) && ranges !== null
    && validByteLimit(value.memoryLimitBytes)
    && (logicalIds === null || includesOnlyCpus(ranges, logicalIds));
}

function validMount(value) {
  return keys(value, mountFields) && [value.containerPath, value.mountType, value.fileSystem]
    .every(item => typeof item === 'string' && item.length > 0)
    && Number.isSafeInteger(value.capacityBytes) && value.capacityBytes > 0
    && Number.isSafeInteger(value.availableBytes) && value.availableBytes >= 0
    && value.availableBytes <= value.capacityBytes;
}

function validContainer(value, requireMounts, requireSamples, hardware) {
  const effectiveSet = keys(value, containerFields) ? parseCpuSet(value.effectiveCpuSet) : null;
  const logicalIds = hardware === null ? null : parseOrderedIds(hardware.logicalCpuMembership);
  return keys(value, containerFields) && typeof value.resource === 'string' && value.resource.length > 0
    && /^[a-f0-9]{64}$/u.test(value.containerId) && /^sha256:[a-f0-9]{64}$/u.test(value.imageId)
    && typeof value.startedAt === 'string' && value.startedAt.length > 0 && value.state === 'running'
    && validCpuMax(value.cpuLimit) && validByteLimit(value.memoryLimitBytes)
    && validQuota(value.effectiveCpuQuota) && effectiveSet !== null
    && (logicalIds === null || includesOnlyCpus(effectiveSet, logicalIds))
    && validByteLimit(value.effectiveMemoryLimitBytes)
    && [value.observedCpuUsageUsec, value.maxObservedCgroupMemoryBytes, value.maxObservedRssBytes]
      .every(item => Number.isSafeInteger(item) && item >= 0)
    && Number.isSafeInteger(value.sampleCount) && value.sampleCount > 0 && value.sampleCount <= maximumSamples
    && (!requireSamples || value.sampleCount >= 2)
    && Array.isArray(value.writableMounts) && value.writableMounts.length <= maximumMounts
    && (!requireMounts || value.writableMounts.length > 0)
    && value.writableMounts.every(validMount);
}

function parseOrderedIds(value) {
  if (typeof value !== 'string' || !/^(?:0|[1-9]\d*)(?:,(?:0|[1-9]\d*))*$/u.test(value)) return null;
  const ids = value.split(',');
  return ids.every((id, index) => Number.isSafeInteger(Number(id)) && (index === 0 || ids[index - 1] < id)) ? ids : null;
}

function parseCpuSet(value) {
  if (typeof value !== 'string' || value.length === 0) return null;
  const ranges = [];
  let previous = -1;
  for (const part of value.split(',')) {
    const fields = part.split('-');
    if (fields.length > 2 || fields.some(item => !/^(?:0|[1-9]\d*)$/u.test(item))) return null;
    const first = Number(fields[0]);
    const last = Number(fields[1] ?? fields[0]);
    if (!Number.isSafeInteger(first) || !Number.isSafeInteger(last) || first > last || first <= previous) return null;
    ranges.push([first, last]);
    previous = last;
  }
  return ranges;
}

function includesOnlyCpus(ranges, logicalIds) {
  const allowed = new Set(logicalIds.map(Number));
  let count = 0;
  for (const [first, last] of ranges) {
    if (last - first + 1 > allowed.size - count) return false;
    for (let cpu = first; cpu <= last; cpu++) {
      if (!allowed.has(cpu)) return false;
      count++;
    }
  }
  return count > 0;
}

function validQuota(value) {
  if (value === 'max') return true;
  if (typeof value !== 'string' || !/^(?:0|[1-9]\d*)(?:\.\d+)?$/u.test(value)) return false;
  return Number.isFinite(Number(value)) && Number(value) > 0;
}

function validByteLimit(value) {
  return value === 'max' || typeof value === 'string'
    && /^(?:0|[1-9]\d*)$/u.test(value) && Number.isSafeInteger(Number(value)) && Number(value) > 0;
}

function validCpuMax(value) {
  if (typeof value !== 'string') return false;
  const fields = value.split(' ');
  return fields.length === 2 && (fields[0] === 'max' || /^(?:0|[1-9]\d*)$/u.test(fields[0])
      && Number.isSafeInteger(Number(fields[0])) && Number(fields[0]) > 0)
    && /^(?:0|[1-9]\d*)$/u.test(fields[1]) && Number.isSafeInteger(Number(fields[1])) && Number(fields[1]) > 0;
}

export function validateServerResourceEvidence(value, sidecarSha256, workerSha256, cell, cohort, jobId, supported) {
  requireGitHub(keys(value, rootFields) && value.schema === schema && hashPattern.test(sidecarSha256)
    && hashPattern.test(workerSha256) && value.workerSha256 === workerSha256 && value.sourceRevision === cohort.sourceRevision
    && value.workflowRunId === String(cohort.runId) && value.runAttempt === String(cohort.attempt)
    && value.jobId === String(jobId) && value.target === cell.target && value.nodeCount === cell.nodeCount
    && value.scenario === cell.scenario && value.profile === cell.profile
    && Array.isArray(value.missingEvidence) && value.missingEvidence.every(item => missingKinds.includes(item))
    && isDeepStrictEqual(value.missingEvidence, [...new Set(value.missingEvidence)].sort())
    && typeof value.qualified === 'boolean' && value.qualified === (value.missingEvidence.length === 0));
  const hardwareMissing = value.missingEvidence.includes('hardwareClass');
  const envelopeMissing = value.missingEvidence.includes('effectiveServerResources');
  requireGitHub(hardwareMissing ? value.hardware === null : validHardware(value.hardware));
  requireGitHub(envelopeMissing ? value.appHostEnvelope === null : validEnvelope(value.appHostEnvelope, value.hardware));
  if (supported) {
    const sampleMissing = value.missingEvidence.includes('serverCpuRss');
    const storageMissing = value.missingEvidence.includes('storageEnvelope');
    requireGitHub(value.containers.length <= cell.nodeCount
      && (sampleMissing || value.containers.length === cell.nodeCount)
      && value.containers.every(item => validContainer(item, !storageMissing, value.qualified, value.hardware))
      && new Set(value.containers.map(item => item.resource)).size === value.containers.length
      && value.containers.length <= maximumContainers);
  } else {
    requireGitHub(value.containers.length === 0 && value.qualified === false
      && value.missingEvidence.includes('serverCpuRss'));
  }
  const comparison = supported && value.qualified ? {
    hardware: value.hardware,
    appHostEnvelope: value.appHostEnvelope,
    containers: value.containers.map(item => ({ effectiveCpuQuota: item.effectiveCpuQuota,
      effectiveCpuSet: item.effectiveCpuSet, effectiveMemoryLimitBytes: item.effectiveMemoryLimitBytes,
      storage: item.writableMounts.map(mount => ({ fileSystem: mount.fileSystem, capacityBytes: mount.capacityBytes }))
        .sort(compareStorage)
    })).sort((left, right) => ordinalCompare(JSON.stringify(left), JSON.stringify(right)))
  } : null;
  const memorySamples = supported && !value.missingEvidence.includes('serverCpuRss')
    ? value.containers.filter(item => item.sampleCount >= 2) : [];
  const observedServerMemory = memorySamples.length === cell.nodeCount ? {
    bytes: memorySamples.reduce((total, item) => total + item.maxObservedRssBytes, 0),
    sampleCount: Math.min(...memorySamples.map(item => item.sampleCount))
  } : null;
  return { sha256: sidecarSha256, qualified: value.qualified, missingEvidence: value.missingEvidence,
    comparison, observedServerMemory };
}

function ordinalCompare(left, right) { return left < right ? -1 : left > right ? 1 : 0; }

function compareStorage(left, right) {
  return ordinalCompare(left.fileSystem, right.fileSystem) || left.capacityBytes - right.capacityBytes;
}

export function validateScaleResourceProof(value, supported) {
  requireGitHub(keys(value, ['sha256', 'qualified', 'missingEvidence', 'comparison', 'observedServerMemory'])
    && hashPattern.test(value.sha256) && typeof value.qualified === 'boolean'
    && Array.isArray(value.missingEvidence) && value.missingEvidence.every(item => missingKinds.includes(item))
    && isDeepStrictEqual(value.missingEvidence, [...new Set(value.missingEvidence)].sort())
    && value.qualified === (value.missingEvidence.length === 0)
    && (supported ? (value.qualified ? value.comparison !== null : value.comparison === null)
      : value.comparison === null && !value.qualified && value.missingEvidence.includes('serverCpuRss'))
    && (value.observedServerMemory === null || keys(value.observedServerMemory, ['bytes', 'sampleCount'])
      && Number.isSafeInteger(value.observedServerMemory.bytes) && value.observedServerMemory.bytes >= 0
      && Number.isSafeInteger(value.observedServerMemory.sampleCount) && value.observedServerMemory.sampleCount >= 2)
    && (supported ? (value.missingEvidence.includes('serverCpuRss') ? value.observedServerMemory === null
      : value.observedServerMemory !== null) : value.observedServerMemory === null));
}

export function requireComparableScaleProfiles(profiles) {
  const groups = new Map();
  for (const profile of profiles) {
    requireGitHub(typeof profile.profile === 'string' && profile.profile.length > 0 && Array.isArray(profile.workers));
    for (const worker of profile.workers) {
      requireGitHub(['measured', 'failed', 'unsupported', 'unsupportedTopology'].includes(worker.disposition)
        && typeof worker.serverResourceQualified === 'boolean');
      if (worker.disposition === 'unsupported' || worker.disposition === 'unsupportedTopology' || !worker.serverResourceQualified) continue;
      requireGitHub(worker.resourceEquivalence !== null && worker.resourceEquivalence !== undefined);
      const key = JSON.stringify([profile.profile, worker.nodeCount, worker.scenario]);
      const previous = groups.get(key);
      if (previous !== undefined) requireGitHub(isDeepStrictEqual(previous, worker.resourceEquivalence));
      else groups.set(key, worker.resourceEquivalence);
    }
  }
}

export { missingKinds as SERVER_RESOURCE_MISSING_KINDS };
