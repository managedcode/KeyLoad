import path from 'node:path';
import { access } from 'node:fs/promises';
import { C, F, fail, nonnegativeInteger, object, positiveInteger, readJson, validSha } from './github-evidence-contracts.mjs';

export async function readRunInventory(root) {
  const pages = await readJson(path.join(root, C.fileRuns), C.errorPagination);
  if (!Array.isArray(pages) || pages.length === 0) fail(C.errorPagination, C.messages.runPaginationEmpty);
  const runs = [];
  const identities = new Set();
  const runNumbers = new Set();
  let expectedCount = null;
  for (const page of pages) {
    if (!object(page) || !Array.isArray(page[F.workflowRuns]) || !nonnegativeInteger(page[F.totalCount])) fail(C.errorPagination, C.messages.runPaginationPageInvalid);
    if (expectedCount !== null && page[F.totalCount] !== expectedCount) fail(C.errorPagination, C.messages.runPaginationCountsDisagree);
    expectedCount = page[F.totalCount];
    for (const run of page[F.workflowRuns]) {
      const identity = `${run?.[F.id]}`;
      const runNumber = run?.[F.runNumber];
      if (!positiveInteger(run?.[F.id]) || !positiveInteger(runNumber) || identities.has(identity) || runNumbers.has(runNumber)) {
        fail(C.errorPagination, C.messages.runPaginationIdentityInvalid);
      }
      identities.add(identity);
      runNumbers.add(runNumber);
      runs.push(run);
    }
  }
  if (runs.length !== expectedCount || runs.length > C.maxRuns) fail(C.errorPagination, C.messages.runPaginationIncomplete);
  return runs;
}

export function validateWorkflow(workflow) {
  if (!object(workflow) || !positiveInteger(workflow[F.id]) || workflow[F.path] !== C.workflowPath ||
      workflow[F.name] !== C.workflowName || workflow[F.state] !== C.active) {
    fail(C.errorWorkflow, C.messages.workflowInvalid);
  }
  return workflow;
}

export function validateRun(run, workflow) {
  const ownRepository = run?.[F.repository];
  const headRepository = run?.[F.headRepository];
  if (!object(run) || !positiveInteger(run[F.id]) || !positiveInteger(run[F.runNumber]) ||
      !positiveInteger(run[F.runAttempt]) || run[F.workflowId] !== workflow[F.id] || run[F.path] !== C.workflowPath ||
      run[F.name] !== C.workflowName || run[F.event] !== C.push || run[F.headBranch] !== C.main || !validSha(run[F.headSha]) ||
      ownRepository?.[F.fullName] !== C.repo || headRepository?.[F.fullName] !== C.repo ||
      ownRepository?.[F.id] !== C.repoId || headRepository?.[F.id] !== C.repoId) {
    fail(C.errorRun, C.messages.runInvalid);
  }
}

export async function selectEvidence(root, mode, requestedRun) {
  if (![C.modeValidate, C.modePublish].includes(mode) || (requestedRun !== null && !C.runIdentifierPattern.test(requestedRun)) ||
      (mode === C.modePublish && requestedRun)) fail(C.errorArgument, C.messages.selectionArgumentsInvalid);
  const workflow = validateWorkflow(await readJson(path.join(root, C.fileWorkflow)));
  const runs = await readRunInventory(root);
  const selectedRuns = requestedRun ? runs.filter(run => `${run[F.id]}` === requestedRun) : runs;
  if (requestedRun && selectedRuns.length !== 1) fail(C.errorRun, C.messages.requestedRunInvalid);
  selectedRuns.sort((left, right) => right[F.runNumber] - left[F.runNumber] || right[F.id] - left[F.id]);
  let pairCount = 0;
  const trail = [];
  for (const summary of selectedRuns) {
    validateRun(summary, workflow);
    for (let attempt = summary[F.runAttempt]; attempt >= 1; attempt--) {
      pairCount++;
      if (pairCount > C.maxPairs) fail(C.errorPagination, C.messages.attemptHistoryTooLarge);
      const pair = await readAttemptPair(root, summary[F.id], attempt, summary, workflow);
      if (pair === null) return { [F.state]: C.stateNeedsAttempt, [F.runIdKey]: summary[F.id], [F.runNumberKey]: summary[F.runNumber],
        [F.runAttemptKey]: attempt, [F.measuredRevision]: summary[F.headSha], trail };
      trail.push(pair);
      const job = findComparisonJob(pair.jobs, summary, attempt);
      if (!job || job[F.conclusion] !== C.success || job[F.status] !== C.completed) continue;
      validateSuccessfulJob(job, summary, attempt);
      return { ...selectionResult(mode, summary, attempt, job), trail };
    }
  }
  return { [F.state]: C.stateUnavailable, [F.publishEligible]: false, [F.reason]: C.noComparison, trail };
}

export async function readAttemptPair(root, runId, attempt, summary, workflow) {
  const base = path.join(C.attempts, `${runId}`, `${attempt}`);
  const runPath = path.join(root, base, C.run);
  const jobsPath = path.join(root, base, C.jobs);
  const [hasRun, hasJobs] = await Promise.all([exists(runPath), exists(jobsPath)]);
  if (!hasRun && !hasJobs) return null;
  if (!hasRun || !hasJobs) fail(C.errorCapture, C.messages.attemptPairRequired);
  const run = await readJson(runPath);
  const jobs = await readJson(jobsPath, C.errorJob);
  validateRun(run, workflow);
  if (run[F.id] !== summary[F.id] || run[F.runNumber] !== summary[F.runNumber] || run[F.runAttempt] !== attempt || run[F.headSha] !== summary[F.headSha]) {
    fail(C.errorRun, C.messages.attemptRunMismatch);
  }
  if (!Array.isArray(jobs)) fail(C.errorJob, C.messages.attemptJobsInvalid);
  return { run, jobs, runPath, jobsPath };
}

export function findComparisonJob(pages, run, attempt) {
  const jobs = flattenPages(pages, F.jobs, C.errorJob);
  const candidates = jobs.filter(job => job?.[F.name] === C.jobName);
  if (candidates.length > 1) fail(C.errorJob, C.messages.comparisonJobAmbiguous);
  const job = candidates[0];
  if (!job) return null;
  if (job[F.runId] !== run[F.id] || job[F.runAttempt] !== attempt || job[F.headSha] !== run[F.headSha] || !positiveInteger(job[F.id])) {
    fail(C.errorJob, C.messages.comparisonJobIdentityMismatch);
  }
  return job;
}

export function validateSuccessfulJob(job, run, attempt) {
  if (!Array.isArray(job[F.steps]) || !job[F.htmlUrl]) fail(C.errorJob, C.messages.comparisonJobIncomplete);
  const matched = [];
  for (const name of C.steps) {
    const steps = job[F.steps].filter(step => step?.[F.name] === name);
    if (steps.length !== 1 || steps[0][F.conclusion] !== C.success || steps[0][F.status] !== C.completed || !positiveInteger(steps[0][F.number])) {
      fail(C.errorJob, C.messages.comparisonStepMissing);
    }
    matched.push({ name, number: steps[0][F.number] });
  }
  if (new Set(matched.map(step => step.number)).size !== C.steps.length || job[F.runId] !== run[F.id] || job[F.runAttempt] !== attempt) {
    fail(C.errorJob, C.messages.comparisonStepsAmbiguous);
  }
  return matched;
}

export function flattenPages(pages, property, errorCode) {
  if (!Array.isArray(pages) || pages.length === 0) fail(errorCode, C.messages.paginationInvalid);
  const flattened = [];
  const identities = new Set();
  let count = null;
  for (const page of pages) {
    if (!object(page) || !Array.isArray(page[property]) || !nonnegativeInteger(page[F.totalCount])) fail(errorCode, C.messages.paginationPageInvalid);
    if (count !== null && page[F.totalCount] !== count) fail(errorCode, C.messages.paginationCountsDisagree);
    count = page[F.totalCount];
    for (const item of page[property]) {
      if (!object(item) || !positiveInteger(item[F.id]) || identities.has(item[F.id])) {
        fail(errorCode, C.messages.paginationIdentityInvalid);
      }
      identities.add(item[F.id]);
      flattened.push(item);
    }
  }
  if (flattened.length !== count) fail(errorCode, C.messages.paginationIncomplete);
  return flattened;
}

export function selectionResult(mode, run, attempt, job) {
  return { [F.state]: C.stateSelected, [F.runIdKey]: run[F.id], [F.runNumberKey]: run[F.runNumber], [F.runAttemptKey]: attempt,
    [F.measuredRevision]: run[F.headSha], [F.comparisonJobId]: job[F.id],
    [F.publishEligible]: mode === C.modePublish, [F.reason]: null };
}

async function exists(file) {
  try { await access(file); return true; }
  catch (error) {
    if (error.code === C.filesystemErrors.missingEntry || error.code === C.filesystemErrors.notDirectory) return false;
    fail(C.errorCapture, C.messages.attemptCannotInspect);
  }
}
