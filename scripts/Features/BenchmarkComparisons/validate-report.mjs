import {
  caseKeyDelimiter,
  caseStatus,
  digestPattern,
  javascriptType,
  legacyProfiles,
  legacyScenarioNames,
  legacySupportedScenarios,
  legacyTargetNames,
  messageLabel,
  replicatedSupport,
  scenarioName,
  scenarioNames,
  schemaField,
  schema3Profiles,
  schemaVersion,
  singleSupport,
  sourceShaPattern,
  targetName,
  topologyName,
  targetNames,
} from './contracts.mjs';
import { validateMeasurement, isFiniteNumber } from './sample-metrics.mjs';
import { messages } from './messages.mjs';

const digestLength = 64;
const requiredLegacySchema = 2;
const requiredProvenanceFields = Object.freeze([schemaField.runId, schemaField.attempt, schemaField.repository, schemaField.ref, schemaField.workflow, schemaField.profile]);
const nonemptyTargetFields = Object.freeze([schemaField.version, schemaField.topology, schemaField.writeAcknowledgement, schemaField.readContract, schemaField.transport, schemaField.authorization]);
const historyTargetCount = legacyTargetNames.length;
const reportRuntimeFields = Object.freeze([schemaField.runId, schemaField.startedAt, schemaField.loadModel, schemaField.hostOs, schemaField.architecture, schemaField.runtime, schemaField.storage]);
const sha256Pattern = new RegExp(`^[a-f0-9]{${digestLength}}$`, 'i');
const reportGuidPattern = /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i;

function addIssue(issues, message) {
  issues.push(message);
}

function hasText(value) {
  return typeof value === javascriptType.string && value.trim().length > 0;
}

function validateBase(report, profileName, metadata, legacy, issues) {
  const expectedVersion = legacy ? requiredLegacySchema : schemaVersion;
  if (!report || typeof report !== javascriptType.object || Array.isArray(report)) {
    addIssue(issues, messages.errors.reportObject(profileName));
    return false;
  }
  if (report[schemaField.schemaVersion] !== expectedVersion) addIssue(issues, messages.errors.schemaVersion(profileName, report[schemaField.schemaVersion]));
  for (const field of reportRuntimeFields) {
    if (!hasText(report[field])) addIssue(issues, messages.errors.reportField(profileName, field));
  }
  if (!reportGuidPattern.test(report[schemaField.runId])) addIssue(issues, messages.errors.reportField(profileName, schemaField.runId));
  if (!Number.isInteger(report[schemaField.logicalProcessors]) || report[schemaField.logicalProcessors] < 1) addIssue(issues, messages.errors.reportField(profileName, schemaField.logicalProcessors));
  if (!isFiniteNumber(Date.parse(report[schemaField.startedAt]))) addIssue(issues, messages.errors.reportField(profileName, schemaField.startedAt));
  if (!hasText(report[schemaField.datasetSha256]) || !sha256Pattern.test(report[schemaField.datasetSha256])) addIssue(issues, messages.errors.reportField(profileName, schemaField.datasetSha256));
  if (!hasText(report[schemaField.sourceRevision]) || !sourceShaPattern.test(report[schemaField.sourceRevision]) || report[schemaField.sourceRevision].toLowerCase() !== metadata.sourceSha.toLowerCase()) {
    addIssue(issues, messages.errors.sourceRevision(profileName));
  }
  if (!Array.isArray(report[schemaField.targets]) || !Array.isArray(report[schemaField.cases]) || !report[schemaField.options] || typeof report[schemaField.options] !== javascriptType.object) {
    addIssue(issues, messages.errors.reportField(profileName, `${schemaField.options}, ${schemaField.targets}, or ${schemaField.cases}`));
    return false;
  }
  if (legacy) return true;
  if (!hasText(report[schemaField.loadGeneratorImage]) || !digestPattern.test(report[schemaField.loadGeneratorImage])) addIssue(issues, messages.errors.imageDigest(profileName, messageLabel.loadGenerator));
  if (!validateProvenance(report[schemaField.provenance], profileName, metadata, issues)) return false;
  return true;
}

function validateProvenance(provenance, profileName, metadata, issues) {
  const actualValues = [provenance?.[schemaField.runId], provenance?.[schemaField.attempt], provenance?.[schemaField.repository], provenance?.[schemaField.ref], provenance?.[schemaField.workflow], provenance?.[schemaField.profile]];
  const expectedValues = [metadata.runId, metadata.attempt, metadata.repository, metadata.ref, metadata.workflow, profileName];
  if (!provenance || typeof provenance !== javascriptType.object || requiredProvenanceFields.some((field, index) => actualValues[index] !== expectedValues[index])) {
    addIssue(issues, messages.errors.provenance(profileName));
    return false;
  }
  return true;
}

function validateOptions(report, profileName, legacy, issues) {
  const profile = (legacy ? legacyProfiles : schema3Profiles)[profileName];
  if (!profile) return false;
  const expectedFields = [...Object.keys(profile.options), schemaField.seed, schemaField.timeoutSeconds];
  if (!legacy) expectedFields.push(schemaField.topology);
  const actualFields = Object.keys(report[schemaField.options]);
  if (actualFields.length !== expectedFields.length || actualFields.some(field => !expectedFields.includes(field))) {
    addIssue(issues, messages.errors.profileOptions(profileName));
    return false;
  }
  for (const [field, expected] of Object.entries(profile.options)) {
    if (report[schemaField.options][field] !== expected) {
      addIssue(issues, messages.errors.profileOptions(profileName));
      return false;
    }
  }
  if (!legacy && report[schemaField.options][schemaField.topology] !== profile.topology) addIssue(issues, messages.errors.profileOptions(profileName));
  if (!Number.isInteger(report[schemaField.options][schemaField.seed]) || !Number.isInteger(report[schemaField.options][schemaField.timeoutSeconds]) || report[schemaField.options][schemaField.timeoutSeconds] < 1) {
    addIssue(issues, messages.errors.profileOptions(profileName));
  }
  return true;
}

function expectedTargets(legacy) {
  return legacy ? legacyTargetNames : targetNames;
}

function validateTargetFields(target, profileName, legacy, profile, issues) {
  if (!target || !hasText(target[schemaField.name]) || nonemptyTargetFields.some(field => !hasText(target[field]))) return false;
  if (legacy && target[schemaField.name] === targetName.keyLoad) return target[schemaField.image] === null;
  if (!hasText(target[schemaField.image]) || !digestPattern.test(target[schemaField.image])) {
    addIssue(issues, messages.errors.imageDigest(profileName, target[schemaField.name]));
    return false;
  }
  if (legacy) return true;
  return validateClusterEvidence(target[schemaField.cluster], target[schemaField.name], profileName, profile.topology, issues);
}

function expectedClusterShape(target, topology) {
  if (target === targetName.keyLoad) return { nodes: 3, dataCopies: 3 };
  if (topology === topologyName.replicated && target !== targetName.neo4j) return { nodes: 3, dataCopies: 3 };
  return { nodes: 1, dataCopies: 1 };
}

function validateClusterEvidence(cluster, targetName, profileName, topology, issues) {
  const expected = expectedClusterShape(targetName, topology);
  const valid = cluster && cluster[schemaField.nodes] === expected.nodes && cluster[schemaField.dataCopies] === expected.dataCopies
    && hasText(cluster[schemaField.state]) && Array.isArray(cluster[schemaField.observations]) && cluster[schemaField.observations].length > 0
    && cluster[schemaField.observations].every(hasText);
  if (!valid) addIssue(issues, messages.errors.clusterEvidence(profileName, targetName));
  return Boolean(valid);
}

function validateTargets(report, profileName, legacy, profile, issues) {
  const expected = expectedTargets(legacy);
  if (report[schemaField.targets].length !== expected.length || new Set(report[schemaField.targets].map(target => target?.[schemaField.name])).size !== expected.length
    || expected.some(name => !report[schemaField.targets].some(target => target?.[schemaField.name] === name))) {
    addIssue(issues, messages.errors.targets(profileName));
    return false;
  }
  for (const target of report[schemaField.targets]) {
    if (!validateTargetFields(target, profileName, legacy, profile, issues)) addIssue(issues, messages.errors.targets(profileName));
  }
  return true;
}

function scenarioSupport(targetName, topology, legacy) {
  if (legacy) return legacySupportedScenarios[targetName] ?? [];
  return (topology === topologyName.replicated ? replicatedSupport : singleSupport)[targetName] ?? [];
}

function caseKey(target, scenario, repetition) {
  return `${target}${caseKeyDelimiter}${scenario}${caseKeyDelimiter}${repetition}`;
}

function validateCaseShape(item, profileName, key, supported, operations, concurrency, payloadBytes, issues) {
  if (!item || !Number.isInteger(item[schemaField.repetition]) || !hasText(item[schemaField.target]) || !hasText(item[schemaField.scenario])) {
    addIssue(issues, messages.errors.caseShape(profileName, key));
    return false;
  }
  if (!supported) {
    const valid = item[schemaField.status] === caseStatus.unsupported && item[schemaField.measurement] === null && Array.isArray(item[schemaField.samples]) && item[schemaField.samples].length === 0 && hasText(item[schemaField.detail]);
    if (!valid) addIssue(issues, messages.errors.unsupported(profileName, key));
    return valid;
  }
  if (item[schemaField.status] !== caseStatus.measured || !Array.isArray(item[schemaField.samples]) || !item[schemaField.measurement] || !hasText(item[schemaField.target])) {
    addIssue(issues, messages.errors.failedCase(profileName, key));
    return false;
  }
  const measurementSummary = validateMeasurement(item[schemaField.measurement], item[schemaField.samples], { operations, concurrency, payloadBytes }, item[schemaField.scenario]);
  if (!measurementSummary || measurementSummary.failures !== 0 || measurementSummary.successes !== operations) {
    addIssue(issues, messages.errors.failedCase(profileName, key));
    return false;
  }
  return true;
}

function validateCases(report, profileName, legacy, profile, issues) {
  const scenarioSet = legacy ? legacyScenarioNames : scenarioNames;
  const targetSet = expectedTargets(legacy);
  const repetitions = report[schemaField.options][schemaField.repetitions];
  const operations = report[schemaField.options][schemaField.operations];
  const concurrency = report[schemaField.options][schemaField.concurrency];
  const payloadBytes = report[schemaField.options][schemaField.payloadBytes];
  if (!Number.isInteger(repetitions) || repetitions < 1 || !Number.isInteger(operations) || operations < 1) {
    addIssue(issues, messages.errors.profileOptions(profileName));
    return 0;
  }
  const casesByKey = new Map();
  for (const item of report[schemaField.cases]) {
    const key = caseKey(item?.[schemaField.target], item?.[schemaField.scenario], item?.[schemaField.repetition]);
    if (casesByKey.has(key)) addIssue(issues, messages.errors.cases(profileName));
    casesByKey.set(key, item);
  }
  const expectedCount = targetSet.length * scenarioSet.length * repetitions;
  if (casesByKey.size !== expectedCount) addIssue(issues, messages.errors.cases(profileName));
  return validateCaseGrid(casesByKey, profileName, targetSet, scenarioSet, repetitions, operations, concurrency, payloadBytes, profile, legacy, issues);
}

function validateCaseGrid(casesByKey, profileName, targets, scenarios, repetitions, operations, concurrency, payloadBytes, profile, legacy, issues) {
  let supportedPerRepetition = 0;
  for (let repetition = 0; repetition < repetitions; repetition++) {
    let supported = 0;
    for (const target of targets) {
      for (const scenario of scenarios) {
        const item = casesByKey.get(caseKey(target, scenario, repetition));
        const expectedSupport = scenarioSupport(target, profile.topology, legacy).includes(scenario);
        if (expectedSupport) supported++;
        validateCaseShape(item, profileName, `${target}/${scenario}/${repetition}`, expectedSupport, operations, concurrency, payloadBytes, issues);
      }
    }
    const expectedCount = legacy ? 20 : profile.topology === topologyName.single ? 35 : 31;
    if (supported !== expectedCount) addIssue(issues, messages.errors.cases(profileName));
    supportedPerRepetition += supported;
  }
  return supportedPerRepetition;
}

export function validateReport(report, profileName, metadata, legacy = false) {
  const issues = [];
  const profile = (legacy ? legacyProfiles : schema3Profiles)[profileName];
  if (!profile || !validateBase(report, profileName, metadata, legacy, issues)) return { issues, supportedCases: 0 };
  validateOptions(report, profileName, legacy, issues);
  validateTargets(report, profileName, legacy, profile, issues);
  const supportedCases = validateCases(report, profileName, legacy, profile, issues);
  return { issues, supportedCases };
}

export function expectedProfileNames(legacy = false) {
  return Object.keys(legacy ? legacyProfiles : schema3Profiles);
}
