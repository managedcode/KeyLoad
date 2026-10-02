import { createHash, randomUUID } from 'node:crypto';
import { lstat, mkdir, mkdtemp, readFile, rename, rm, writeFile } from 'node:fs/promises';
import { dirname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { artifactLayout, chartMetric, dataEncoding, digestPattern, fileSystemSignal, hashAlgorithm, javascriptType, legacyProfiles, messageLabel, outputFile, outputFormat, profileName, publisherField, repositoryName, schema3Profiles, schemaField, sourceShaPattern, summaryField } from './contracts.mjs';
import { renderQueueChart, renderScenarioChart, summarizeProfile } from './charts.mjs';
import { readEvidence } from './read-input.mjs';
import { expectedProfileNames, validateReport } from './validate-report.mjs';
import { messages } from './messages.mjs';

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(scriptDirectory, artifactLayout.scriptToRepositoryRoot);
const rawBaseUrl = `https://raw.githubusercontent.com/${repositoryName}/${artifactLayout.branch}`;
const optionDefaults = Object.freeze({ seed: 1729, timeoutSeconds: 30 });
const pairedProfiles = Object.freeze([
  [profileName.smoke, profileName.smokeSingle, profileName.smokeReplicated],
  [profileName.json1kC8, profileName.json1kC8Single, profileName.json1kC8Replicated],
  [profileName.json16kC4, profileName.json16kC4Single, profileName.json16kC4Replicated],
]);
function rawSha256(bytes) {
  return createHash(hashAlgorithm.sha256).update(bytes).digest(dataEncoding.hex);
}

function addUrlPath(...parts) {
  return parts.map(part => String(part).replace(/^\/+|\/+$/g, '')).join(artifactLayout.pathSeparator);
}

function issueForProfile(input, metadata) {
  return validateReport(input.report, input.profile, metadata, metadata.legacy);
}

function validateProfileSet(inputs, legacy) {
  const expected = expectedProfileNames(legacy).sort();
  const actual = inputs.map(input => input.profile).sort();
  if (expected.length !== actual.length || expected.some((profile, index) => profile !== actual[index])) {
    throw new Error(messages.errors.profileSet({ expected, actual }));
  }
}

function validateAllReports(inputs, metadata) {
  const issues = [];
  let caseCount = 0;
  const reports = new Map();
  for (const input of inputs) {
    const result = issueForProfile(input, metadata);
    issues.push(...result.issues);
    caseCount += input.report[schemaField.cases]?.length ?? 0;
    reports.set(input.profile, input.report);
  }
  if (issues.length > 0) throw new Error(`${messages.errors.failureSummary(issues.length)}${messages.format.newline}${issues.join(messages.format.newline)}`);
  validateProfileCohorts(reports, metadata.legacy);
  return { caseCount, reports };
}

function validateProfileCohorts(reports, legacy) {
  if (legacy) return;
  const imageDigests = new Map();
  let loadGeneratorImage = null;
  for (const [profileName, report] of reports) {
    if (loadGeneratorImage !== null && report[schemaField.loadGeneratorImage] !== loadGeneratorImage) throw new Error(messages.errors.targets(profileName));
    loadGeneratorImage = report[schemaField.loadGeneratorImage];
    for (const target of report[schemaField.targets]) {
      const targetName = target[schemaField.name];
      const image = imageDigests.get(targetName);
      if (image !== undefined && image !== target[schemaField.image]) throw new Error(messages.errors.imageDigest(profileName, targetName));
      imageDigests.set(targetName, target[schemaField.image]);
    }
  }
  for (const profile of Object.keys(schema3Profiles)) {
    if (!digestPattern.test(reports.get(profile)?.[schemaField.loadGeneratorImage] ?? '')) throw new Error(messages.errors.imageDigest(profile, messageLabel.loadGenerator));
  }
  validatePairedCorpora(reports);
}

function validatePairedCorpora(reports) {
  for (const [base, singleProfile, replicatedProfile] of pairedProfiles) {
    const single = reports.get(singleProfile);
    const replicated = reports.get(replicatedProfile);
    if (!single || !replicated || single[schemaField.datasetSha256] !== replicated[schemaField.datasetSha256]) throw new Error(messages.errors.targets(base));
    for (const field of Object.keys(single[schemaField.options])) {
      if (field !== schemaField.topology && single[schemaField.options][field] !== replicated[schemaField.options][field]) throw new Error(messages.errors.profileOptions(base));
    }
  }
}

function enforceOptionDefaults(inputs) {
  for (const input of inputs) {
    for (const [field, expected] of Object.entries(optionDefaults)) {
      if (input.report[schemaField.options]?.[field] !== expected) throw new Error(messages.errors.profileOptions(input.profile));
    }
  }
}

function makeRunPaths(outputRoot, metadata) {
  const runRelative = join(artifactLayout.historyDirectory, metadata.sourceSha, String(metadata.runId), String(metadata.attempt));
  return { runRoot: join(outputRoot, runRelative), runRelative };
}

function publicRawUrl(runRelative, profile) {
  return `${rawBaseUrl}/${addUrlPath(runRelative, profile, outputFile.results)}`;
}

function provenanceDocument(metadata, inputs, runRelative, legacy) {
  return {
    [schemaField.sourceSha]: metadata.sourceSha,
    [schemaField.runId]: metadata.runId,
    [schemaField.attempt]: metadata.attempt,
    [schemaField.repository]: metadata.repository,
    [schemaField.ref]: metadata.ref,
    [schemaField.workflow]: metadata.workflow,
    [schemaField.event]: metadata.event,
    [schemaField.conclusion]: metadata.conclusion,
    [publisherField.trustedWorkflowGateRequired]: true,
    [publisherField.publisherNote]: messages.publisherNotes.trust,
    [schemaField.legacyBaseline]: legacy,
    [publisherField.qualificationNote]: legacy ? messages.publisherNotes.legacy : messages.publisherNotes.schema3,
    [schemaField.profiles]: inputs.map(input => ({ [schemaField.profile]: input.profile, [schemaField.rawUrl]: publicRawUrl(runRelative, input.profile), [schemaField.sha256]: rawSha256(input.raw) })),
  };
}

function topologyLabel(input, metadata) {
  if (metadata.legacy) return legacyProfiles[input.profile].displayTopology;
  return schema3Profiles[input.profile].topology;
}

function profileSummary(input, metadata) {
  return summarizeProfile(input.report, input.profile, topologyLabel(input, metadata), metadata.legacy);
}

function writeJson(path, value) {
  return writeOutputFile(path, `${JSON.stringify(value, null, outputFormat.jsonIndent)}${outputFormat.newline}`, dataEncoding.utf8);
}

async function assertNoOutputSymlinks(path) {
  const absolutePath = resolve(path);
  let current = sep;
  for (const part of absolutePath.slice(sep.length).split(sep).filter(Boolean)) {
    current = join(current, part);
    try {
      if ((await lstat(current)).isSymbolicLink()) throw new Error(messages.errors.unsafeOutput);
    } catch (error) {
      if (error.code === fileSystemSignal.notFound) break;
      throw error;
    }
  }
}

async function writeOutputFile(path, bytes, options) {
  await assertNoOutputSymlinks(path);
  return writeFile(path, bytes, options);
}

async function makeOutputDirectory(path) {
  await assertNoOutputSymlinks(path);
  return mkdir(path, { recursive: true });
}

async function assertRunDoesNotExist(runRoot) {
  try {
    await lstat(runRoot);
    throw new Error(messages.errors.historyExists(runRoot));
  } catch (error) {
    if (error.code !== fileSystemSignal.notFound) throw error;
  }
}

async function writeProfileHistory(runRoot, runRelative, input, metadata) {
  const profileDirectory = join(runRoot, input.profile);
  await makeOutputDirectory(profileDirectory);
  const rawPath = join(profileDirectory, outputFile.results);
  await writeOutputFile(rawPath, input.raw, { flag: fileSystemSignal.createNew });
  const rawSha = rawSha256(input.raw);
  const rawUrl = publicRawUrl(runRelative, input.profile);
  await writeJson(join(profileDirectory, outputFile.checksum), { [publisherField.file]: outputFile.results, [schemaField.sha256]: rawSha, [schemaField.sourceSha]: metadata.sourceSha, [schemaField.rawUrl]: rawUrl });
  const summary = profileSummary(input, metadata);
  await writeJson(join(profileDirectory, outputFile.summary), summary);
  const charts = await writeProfileCharts(profileDirectory, input, summary, metadata, rawUrl);
  return { profile: input.profile, rawPath: relative(runRoot, rawPath).split(sep).join(artifactLayout.pathSeparator), rawUrl, rawSha256: rawSha, charts, summary };
}

async function writeProfileCharts(profileDirectory, input, summary, metadata, rawUrl) {
  const profile = (metadata.legacy ? legacyProfiles : schema3Profiles)[input.profile];
  if (!profile.charts) return [];
  const chartDirectory = join(profileDirectory, artifactLayout.chartsDirectory);
  await makeOutputDirectory(chartDirectory);
  const charts = [
    [outputFile.throughputChart, renderScenarioChart(summary, chartMetric.throughput, { ...metadata, [schemaField.targets]: input.report[schemaField.targets] }, rawUrl)],
    [outputFile.p99Chart, renderScenarioChart(summary, chartMetric.p99, { ...metadata, [schemaField.targets]: input.report[schemaField.targets] }, rawUrl)],
    [outputFile.queueChart, renderQueueChart(summary, { ...metadata, [schemaField.targets]: input.report[schemaField.targets] }, rawUrl)],
  ];
  for (const [fileName, svg] of charts) await writeOutputFile(join(chartDirectory, fileName), svg, dataEncoding.utf8);
  return charts.map(([fileName]) => join(input.profile, artifactLayout.chartsDirectory, fileName));
}

async function writeLatestContents(outputRoot, latestRoot, metadata, profileResults, summary, provenance, runRelative) {
  const chartDirectory = join(latestRoot, artifactLayout.chartsDirectory);
  const resultsDirectory = join(latestRoot, artifactLayout.resultsDirectory);
  await makeOutputDirectory(chartDirectory);
  await makeOutputDirectory(resultsDirectory);
  const links = [];
  for (const result of profileResults) {
    const pointer = { [schemaField.profile]: result.profile, [publisherField.url]: result.rawUrl, [schemaField.sha256]: result.rawSha256, [schemaField.sourceSha]: metadata.sourceSha };
    await writeJson(join(resultsDirectory, `${result.profile}${outputFormat.jsonSuffix}`), pointer);
    for (const chartPath of result.charts) {
      const source = join(outputRoot, runRelative, chartPath);
      const destination = join(chartDirectory, `${result.profile}-${chartPath.split(artifactLayout.pathSeparator).at(-1)}`);
      await writeOutputFile(destination, await readFile(source), dataEncoding.utf8);
      links.push({ [schemaField.profile]: result.profile, [schemaField.name]: destination.slice(chartDirectory.length + 1), [publisherField.path]: join(artifactLayout.chartsDirectory, destination.slice(chartDirectory.length + 1)) });
    }
  }
  await writeJson(join(latestRoot, outputFile.summary), summary);
  await writeJson(join(latestRoot, outputFile.provenance), provenance);
  await writeJson(join(latestRoot, outputFile.index), { [schemaField.sourceSha]: metadata.sourceSha, [schemaField.runId]: metadata.runId, [schemaField.attempt]: metadata.attempt, [publisherField.results]: profileResults.map(({ profile, rawUrl, rawSha256 }) => ({ [schemaField.profile]: profile, [schemaField.rawUrl]: rawUrl, [schemaField.sha256]: rawSha256 })), [publisherField.charts]: links });
}

async function assertLatestDoesNotRegress(outputRoot, metadata) {
  const latestRoot = join(outputRoot, artifactLayout.latestDirectory);
  const provenancePath = join(latestRoot, outputFile.provenance);
  await assertNoOutputSymlinks(latestRoot);
  try {
    await lstat(latestRoot);
  } catch (error) {
    if (error.code === fileSystemSignal.notFound) return;
    throw error;
  }
  await assertNoOutputSymlinks(provenancePath);
  let previous;
  try {
    previous = JSON.parse(await readFile(provenancePath, dataEncoding.utf8));
  } catch (error) {
    throw new Error(messages.errors.latestUnreadable(provenancePath));
  }
  if (typeof previous[schemaField.sourceSha] !== javascriptType.string || !sourceShaPattern.test(previous[schemaField.sourceSha])
    || !Number.isSafeInteger(previous[schemaField.runId]) || previous[schemaField.runId] < 1
    || !Number.isInteger(previous[schemaField.attempt]) || previous[schemaField.attempt] < 1) {
    throw new Error(messages.errors.latestInvalid(provenancePath));
  }
  if (previous[schemaField.sourceSha].toLowerCase() !== metadata.sourceSha.toLowerCase()) return;
  const priorRun = previous[schemaField.runId];
  const priorAttempt = previous[schemaField.attempt];
  const regresses = !Number.isSafeInteger(priorRun) || !Number.isInteger(priorAttempt)
    || metadata.runId < priorRun || (metadata.runId === priorRun && metadata.attempt < priorAttempt);
  if (regresses) throw new Error(messages.errors.latestWouldRegress);
}

async function replaceLatest(outputRoot, metadata, profileResults, summary, provenance, runRelative) {
  const stagingRoot = await mkdtemp(join(outputRoot, artifactLayout.latestStagingPrefix));
  const latestRoot = join(outputRoot, artifactLayout.latestDirectory);
  const backupRoot = join(outputRoot, `${artifactLayout.latestBackupPrefix}${randomUUID()}`);
  let movedPrevious = false;
  try {
    await writeLatestContents(outputRoot, stagingRoot, metadata, profileResults, summary, provenance, runRelative);
    await assertNoOutputSymlinks(latestRoot);
    try {
      await lstat(latestRoot);
      await rename(latestRoot, backupRoot);
      movedPrevious = true;
    } catch (error) {
      if (error.code !== fileSystemSignal.notFound) throw error;
    }
    try {
      await rename(stagingRoot, latestRoot);
    } catch (error) {
      if (movedPrevious) await rename(backupRoot, latestRoot);
      throw error;
    }
    if (movedPrevious) await rm(backupRoot, { recursive: true });
  } finally {
    await rm(stagingRoot, { recursive: true, force: true });
  }
}

async function publish(evidence) {
  validateProfileSet(evidence.inputs, evidence.metadata.legacy);
  enforceOptionDefaults(evidence.inputs);
  const { caseCount, reports } = validateAllReports(evidence.inputs, evidence.metadata);
  await makeOutputDirectory(evidence.outputRoot);
  await assertLatestDoesNotRegress(evidence.outputRoot, evidence.metadata);
  const paths = makeRunPaths(evidence.outputRoot, evidence.metadata);
  await assertRunDoesNotExist(paths.runRoot);
  await makeOutputDirectory(paths.runRoot);
  const profileResults = [];
  for (const input of evidence.inputs) profileResults.push(await writeProfileHistory(paths.runRoot, paths.runRelative, input, evidence.metadata));
  const summary = {
    [schemaField.sourceSha]: evidence.metadata.sourceSha,
    [schemaField.runId]: evidence.metadata.runId,
    [schemaField.attempt]: evidence.metadata.attempt,
    [schemaField.repository]: evidence.metadata.repository,
    [schemaField.workflow]: evidence.metadata.workflow,
    [schemaField.conclusion]: evidence.metadata.conclusion,
    [schemaField.event]: evidence.metadata.event,
    [schemaField.ref]: evidence.metadata.ref,
    [schemaField.legacyBaseline]: evidence.metadata.legacy,
    [schemaField.qualifiesSchema3]: !evidence.metadata.legacy,
    [publisherField.profileCount]: evidence.inputs.length,
    [publisherField.caseCount]: caseCount,
    [schemaField.profiles]: profileResults.map(({ profile, rawUrl, rawSha256, charts, summary: profileData }) => ({ [schemaField.profile]: profile, [schemaField.rawUrl]: rawUrl, [publisherField.rawSha256]: rawSha256, [publisherField.chartPaths]: charts, [summaryField.supportedCaseCount]: profileData[summaryField.supportedCaseCount], [summaryField.unavailableCaseCount]: profileData[summaryField.unavailableCaseCount], [schemaField.corpusSha256]: profileData[schemaField.corpusSha256] })),
  };
  const provenance = provenanceDocument(evidence.metadata, evidence.inputs, paths.runRelative, evidence.metadata.legacy);
  await writeJson(join(paths.runRoot, outputFile.summary), summary);
  await writeJson(join(paths.runRoot, outputFile.provenance), provenance);
  await replaceLatest(evidence.outputRoot, evidence.metadata, profileResults, summary, provenance, paths.runRelative);
  process.stdout.write(`${messages.errors.success(evidence.inputs.length, caseCount, profileResults.length + profileResults.reduce((total, profile) => total + profile.charts.length, 0))}${messages.format.newline}`);
  process.stdout.write(`${messages.status.history}${paths.runRoot}${messages.format.newline}`);
  process.stdout.write(`${messages.status.sourceRevision}${[...reports.values()][0][schemaField.sourceRevision]}${messages.format.newline}`);
}

async function main() {
  const evidence = await readEvidence(process.argv.slice(2), repositoryRoot, sourceShaPattern);
  await publish(evidence);
}

main().catch(error => {
  process.stderr.write(`${error.message}${messages.format.newline}${messages.usage}${messages.format.newline}`);
  process.exitCode = 1;
});
