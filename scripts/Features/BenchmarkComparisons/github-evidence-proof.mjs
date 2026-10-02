import path from 'node:path';
import { C, F, digestFile, fail, object, parseTime, positiveInteger, readJson, same, validDigest, validSha } from './github-evidence-contracts.mjs';
import { findComparisonJob, flattenPages, readAttemptPair, readRunInventory, selectEvidence, validateRun, validateWorkflow, validateSuccessfulJob } from './github-evidence-runs.mjs';

export async function proveEvidence(root, mode, requestedRun, siteRevision, workflowRevision) {
  if (!validSha(siteRevision) || !validSha(workflowRevision)) fail(C.errorSource, C.messages.sourceRevisionsInvalid);
  const workflow = validateWorkflow(await readJson(path.join(root, C.fileWorkflow)));
  const selection = await selectEvidence(root, mode, requestedRun);
  if (selection[F.state] !== C.stateSelected) fail(C.errorRun, C.messages.selectionNotSuccessful);
  const runSummaries = await readRunInventory(root);
  const summary = runSummaries.find(run => run[F.id] === selection[F.runIdKey]);
  validateRun(summary, workflow);
  const pair = await readAttemptPair(root, summary[F.id], selection[F.runAttemptKey], summary, workflow);
  const topRun = await readJson(path.join(root, C.fileRun));
  const topJobs = await readJson(path.join(root, C.fileJobs), C.errorJob);
  const topArtifacts = await readJson(path.join(root, C.fileArtifacts), C.errorArtifact);
  assertRunIdentity(topRun, pair.run);
  const job = findTopJob(topJobs, selection);
  const attemptedJob = findComparisonJob(pair.jobs, pair.run, selection[F.runAttemptKey]);
  if (!attemptedJob || attemptedJob[F.conclusion] !== C.success || attemptedJob[F.status] !== C.completed) fail(C.errorJob, C.messages.attemptedJobUnsuccessful);
  const attemptSteps = validateSuccessfulJob(attemptedJob, pair.run, selection[F.runAttemptKey]);
  const steps = validateTopJob(job, pair.run, selection[F.runAttemptKey]);
  assertJobAgreement(job, attemptedJob, steps, attemptSteps);
  const artifact = validateArtifact(topArtifacts, pair.run, job, workflow);
  const metadataFiles = await hashMetadata(root, selection.trail ?? [], pair);
  return buildReceipt(workflow, pair.run, job, steps, artifact, metadataFiles, mode, siteRevision, workflowRevision);
}

export async function verifyArchive(receiptPath, archivePath) {
  const receipt = await readJson(receiptPath, C.errorArchive);
  if (!validMetadataReceipt(receipt) || !object(receipt[F.artifactKey])) fail(C.errorArchive, C.messages.metadataReceiptInvalid);
  const actual = await digestFile(archivePath, C.maxArchiveBytes);
  if (actual[F.bytes] !== receipt[F.artifactKey][F.sizeBytes] || `${C.digestPrefix}${actual[F.sha256]}` !== receipt[F.artifactKey][F.artifactDigest]) {
    fail(C.errorArchive, C.messages.archiveMismatch);
  }
  return { ...receipt, [F.state]: C.stateArchive, [F.archiveKey]: actual };
}

export async function verifyFreshness(beforePath, afterPath) {
  const before = await readJson(beforePath, C.errorFreshness);
  const after = await readJson(afterPath, C.errorFreshness);
  if (!validArchiveReceipt(before) || !validMetadataReceipt(after) || before[F.mode] !== C.modePublish ||
      after[F.mode] !== C.modePublish || before[F.publishEligible] !== true || after[F.publishEligible] !== true ||
      before[F.siteRevision] !== after[F.siteRevision] || !same(measurementTuple(before), measurementTuple(after))) {
    fail(C.errorFreshness, C.messages.freshnessMismatch);
  }
  return { [F.fresh]: true, [F.state]: C.stateMetadata, [F.siteRevision]: after[F.siteRevision],
    [F.runKey]: after[F.runKey], [F.comparisonJobId]: after[F.jobKey][F.jobId], [F.id]: after[F.artifactKey][F.id] };
}

export function validateArtifact(pages, run, job, workflow) {
  const artifacts = flattenPages(pages, F.artifacts, C.errorArtifact);
  const matches = artifacts.filter(item => item?.[F.name] === C.artifactName);
  if (matches.length !== 1) fail(C.errorArtifact, C.messages.artifactNotUnique);
  const artifact = matches[0];
  const workflowRun = artifact[F.workflowRun];
  const created = parseTime(artifact[F.createdAt], C.errorArtifact, C.messages.artifactTimestampInvalid);
  const start = parseTime(job[F.startedAt], C.errorArtifact, C.messages.jobStartTimestampInvalid);
  const end = parseTime(job[F.completedAt], C.errorArtifact, C.messages.jobCompletionTimestampInvalid);
  if (artifact[F.expired] !== false || !positiveInteger(artifact[F.id]) || !positiveInteger(artifact[F.sizeInBytes]) ||
      artifact[F.sizeInBytes] > C.maxArchiveBytes || !validDigest(artifact[F.digest]) ||
      !object(workflowRun) || workflowRun[F.id] !== run[F.id] || workflowRun[F.repositoryId] !== C.repoId ||
      workflowRun[F.headRepositoryId] !== C.repoId || workflowRun[F.headBranch] !== C.main ||
      workflowRun[F.headSha] !== run[F.headSha] || workflow[F.id] !== run[F.workflowId] || start > end || created < start || created > end) {
    fail(C.errorArtifact, C.messages.artifactInvalid);
  }
  return artifact;
}

function findTopJob(pages, selection) {
  const jobs = flattenPages(pages, F.jobs, C.errorJob);
  const comparisons = jobs.filter(job => job?.[F.name] === C.jobName);
  const matches = comparisons.filter(job => job?.[F.id] === selection[F.comparisonJobId]);
  if (comparisons.length !== 1 || matches.length !== 1) fail(C.errorJob, C.messages.topComparisonJobNotUnique);
  return matches[0];
}

function validateTopJob(job, run, attempt) {
  if (job[F.runId] !== run[F.id] || job[F.runAttempt] !== attempt || job[F.headSha] !== run[F.headSha] ||
      job[F.status] !== C.completed || job[F.conclusion] !== C.success || !Array.isArray(job[F.steps])) {
    fail(C.errorJob, C.messages.topComparisonJobMismatch);
  }
  const validated = [];
  for (const name of C.steps) {
    const matches = job[F.steps].filter(step => step?.[F.name] === name);
    if (matches.length !== 1 || matches[0][F.status] !== C.completed || matches[0][F.conclusion] !== C.success ||
        !positiveInteger(matches[0][F.number])) fail(C.errorJob, C.messages.topComparisonStepsInvalid);
    validated.push({ [F.name]: name, [F.number]: matches[0][F.number] });
  }
  return validated;
}

function assertRunIdentity(top, selected) {
  if (!object(top) || top[F.id] !== selected[F.id] || top[F.runNumber] !== selected[F.runNumber] ||
      top[F.runAttempt] !== selected[F.runAttempt] || top[F.headSha] !== selected[F.headSha] ||
      top[F.workflowId] !== selected[F.workflowId] || top[F.path] !== selected[F.path] || top[F.event] !== selected[F.event] ||
      top[F.headBranch] !== selected[F.headBranch] || top[F.repository]?.[F.id] !== selected[F.repository]?.[F.id] ||
      top[F.headRepository]?.[F.id] !== selected[F.headRepository]?.[F.id]) {
    fail(C.errorRun, C.messages.topRunMismatch);
  }
}

function assertJobAgreement(top, attempt, topSteps, attemptSteps) {
  const fields = [F.id, F.name, F.runId, F.runAttempt, F.headSha, F.status, F.conclusion, F.startedAt, F.completedAt, F.htmlUrl];
  if (fields.some(field => top[field] !== attempt[field]) || JSON.stringify(topSteps) !== JSON.stringify(attemptSteps)) {
    fail(C.errorJob, C.messages.topJobMismatch);
  }
}

async function hashMetadata(root, trail, selectedPair) {
  const relatives = new Set(C.files);
  for (const pair of trail) {
    if (pair?.runPath && pair?.jobsPath) {
      relatives.add(path.relative(root, pair.runPath));
      relatives.add(path.relative(root, pair.jobsPath));
    }
  }
  if (selectedPair.runPath && selectedPair.jobsPath) {
    relatives.add(path.relative(root, selectedPair.runPath));
    relatives.add(path.relative(root, selectedPair.jobsPath));
  }
  const files = [];
  for (const relative of [...relatives].sort()) {
    const actual = await digestFile(path.join(root, relative), C.maxMetadataBytes, C.errorCapture);
    files.push({ path: relative.split(path.sep).join('/'), sha256: actual.sha256 });
  }
  return files;
}

function buildReceipt(workflow, run, job, steps, artifact, metadataFiles, mode, siteRevision, workflowRevision) {
  return { [F.schemaVersion]: 1, [F.state]: C.stateMetadata, [F.mode]: mode, [F.publishEligible]: mode === C.modePublish,
    [F.repository]: { [F.fullNameKey]: C.repo, [F.id]: C.repoId }, [F.workflowKey]: { [F.id]: workflow[F.id], [F.path]: workflow[F.path] },
      [F.runKey]: { [F.id]: run[F.id], [F.number]: run[F.runNumber], [F.attempt]: run[F.runAttempt], [F.url]: run[F.htmlUrl] },
    [F.siteRevision]: siteRevision, [F.measuredRevision]: run[F.headSha], [F.controlRevision]: workflowRevision,
    [F.jobKey]: { [F.jobId]: job[F.id], [F.url]: job[F.htmlUrl], [F.jobStartedAt]: job[F.startedAt], [F.jobCompletedAt]: job[F.completedAt], [F.comparisonSteps]: steps },
    [F.artifactKey]: { [F.id]: artifact[F.id], [F.artifactName]: artifact[F.name], [F.sizeBytes]: artifact[F.sizeInBytes],
      [F.artifactDigest]: artifact[F.digest], [F.artifactCreatedAt]: artifact[F.createdAt] }, [F.metadataFiles]: metadataFiles };
}

function measurementTuple(receipt) {
  return { [F.measuredRevision]: receipt[F.measuredRevision], [F.runKey]: receipt[F.runKey],
    [F.jobKey]: receipt[F.jobKey], [F.artifactKey]: receipt[F.artifactKey] };
}

function validMetadataReceipt(receipt, state = C.stateMetadata) {
  return object(receipt) && receipt[F.schemaVersion] === 1 && receipt[F.state] === state &&
    [C.modeValidate, C.modePublish].includes(receipt[F.mode]) && receipt[F.publishEligible] === (receipt[F.mode] === C.modePublish) &&
    validSha(receipt[F.siteRevision]) && validSha(receipt[F.measuredRevision]) && validSha(receipt[F.controlRevision]) &&
    object(receipt[F.runKey]) && positiveInteger(receipt[F.runKey][F.id]) && positiveInteger(receipt[F.runKey][F.number]) &&
    positiveInteger(receipt[F.runKey][F.attempt]) && validGitHubRunUrl(receipt[F.runKey][F.url], receipt[F.runKey][F.id]) &&
    object(receipt[F.repository]) && receipt[F.repository][F.fullNameKey] === C.repo && receipt[F.repository][F.id] === C.repoId &&
    object(receipt[F.workflowKey]) && positiveInteger(receipt[F.workflowKey][F.id]) && receipt[F.workflowKey][F.path] === C.workflowPath &&
    object(receipt[F.jobKey]) && positiveInteger(receipt[F.jobKey][F.jobId]) &&
    receipt[F.jobKey][F.url] === `${C.repoWebBase}/actions/runs/${receipt[F.runKey][F.id]}/job/${receipt[F.jobKey][F.jobId]}` &&
    typeof receipt[F.jobKey][F.jobStartedAt] === C.valueTypes.string && typeof receipt[F.jobKey][F.jobCompletedAt] === C.valueTypes.string && Array.isArray(receipt[F.jobKey][F.comparisonSteps]) &&
    object(receipt[F.artifactKey]) && positiveInteger(receipt[F.artifactKey][F.id]) &&
    receipt[F.artifactKey][F.artifactName] === C.artifactName && positiveInteger(receipt[F.artifactKey][F.sizeBytes]) &&
    validDigest(receipt[F.artifactKey][F.artifactDigest]) && typeof receipt[F.artifactKey][F.artifactCreatedAt] === C.valueTypes.string &&
    Array.isArray(receipt[F.metadataFiles]);
}

function validArchiveReceipt(receipt) {
  return validMetadataReceipt(receipt, C.stateArchive) && object(receipt[F.archiveKey]) &&
    validDigest(`${C.digestPrefix}${receipt[F.archiveKey][F.sha]}`) && positiveInteger(receipt[F.archiveKey][F.bytes]) &&
    receipt[F.archiveKey][F.bytes] === receipt[F.artifactKey][F.sizeBytes] &&
    `${C.digestPrefix}${receipt[F.archiveKey][F.sha]}` === receipt[F.artifactKey][F.artifactDigest];
}

function validGitHubRunUrl(value, runId) {
  return value === `${C.repoWebBase}/actions/runs/${runId}`;
}
