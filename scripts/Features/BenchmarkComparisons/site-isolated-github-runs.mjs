import { access } from 'node:fs/promises';
import path from 'node:path';
import { GH, positive, shaPattern } from './isolated-github-contract.mjs';
import { readJson } from './isolated-github-files.mjs';
import { flattenPages, uniqueNamed, validateJobIdentity, validateSuccessfulJob } from './isolated-github-validation.mjs';
import { SITE_GH, exact, isUnsupportedSiteSource, requireSite, siteAggregateSteps, siteEvidencePlans } from './site-isolated-github-contract.mjs';

export function validateSiteWorkflow(workflow) {
  requireSite(workflow?.path === GH.workflowPath && workflow.name === GH.workflow && workflow.state === 'active' && positive(workflow.id));
  return workflow;
}

export function validateSiteRun(run, workflow) {
  requireSite(positive(run?.id) && positive(run.run_number) && positive(run.run_attempt) && run.workflow_id === workflow.id
    && run.path === GH.workflowPath && run.name === GH.workflow && SITE_GH.producerEvents.includes(run.event) && run.head_branch === 'main'
    && /^[a-f0-9]{40}$/.test(run.head_sha ?? '') && run.repository?.id === SITE_GH.repositoryId
    && run.head_repository?.id === SITE_GH.repositoryId && run.repository.full_name === SITE_GH.repository
    && run.head_repository.full_name === SITE_GH.repository && run.html_url === `https://github.com/${SITE_GH.repository}/actions/runs/${run.id}`);
  return run;
}

export function flattenSiteRuns(pages) {
  requireSite(Array.isArray(pages) && pages.length > 0 && pages.length <= GH.pages);
  const total = pages[0]?.total_count;
  requireSite(Number.isSafeInteger(total) && total >= 0 && total <= SITE_GH.items && pages.length === Math.max(1, Math.ceil(total / GH.pageSize)));
  const runs = [];
  const ids = new Set();
  const numbers = new Set();
  for (const [index, page] of pages.entries()) {
    requireSite(page.total_count === total && Array.isArray(page.workflow_runs)
      && page.workflow_runs.length === Math.min(GH.pageSize, total - index * GH.pageSize));
    for (const run of page.workflow_runs) {
      requireSite(positive(run.id) && positive(run.run_number) && !ids.has(run.id) && !numbers.has(run.run_number));
      ids.add(run.id); numbers.add(run.run_number); runs.push(run);
    }
  }
  return runs;
}

export function selectLatestSiteProducer(runs, workflow) {
  const completed = listSiteProducers(runs, workflow).filter(run => !isUnsupportedSiteSource(run.head_sha));
  const run = completed[0];
  requireSite(run !== undefined && SITE_GH.producerConclusions.includes(run.conclusion));
  return siteProducer(run);
}

export function listSiteProducers(runs, workflow) {
  const completed = runs.map(run => validateSiteRun(run, workflow))
    .filter(run => run.status === 'completed' && SITE_GH.producerConclusions.includes(run.conclusion));
  completed.sort((left, right) => right.run_number - left.run_number);
  return completed;
}

const siteProducer = run => ({ runId: run.id, attempt: run.run_attempt, sourceRevision: run.head_sha,
  event: run.event, conclusion: run.conclusion });

export const siteCohort = run => ({ sourceRevision: run.head_sha, runId: run.id, attempt: run.run_attempt,
  repository: SITE_GH.repository, ref: 'refs/heads/main', workflow: GH.workflow, profile: siteEvidencePlans()[0].profile });

async function readPair(input, summary, attempt, workflow) {
  const root = path.join(input, SITE_GH.metadata, 'attempts', String(summary.id), String(attempt));
  const names = ['run-attempt.json', 'jobs-pages.json'];
  const present = await Promise.all(names.map(name => access(path.join(root, name)).then(() => true, error => {
    if (error.code === 'ENOENT') return false;
    throw error;
  })));
  requireSite(present[0] === present[1]);
  if (!present[0]) return null;
  const run = validateSiteRun(await readJson(path.join(root, names[0])), workflow);
  requireSite(run.id === summary.id && run.run_number === summary.run_number && run.head_sha === summary.head_sha
    && run.run_attempt === attempt && run.event === summary.event);
  const jobs = flattenPages(await readJson(path.join(root, names[1])), 'jobs');
  return selectSiteAggregateJob(run, jobs);
}

export function selectSiteAggregateJob(run, jobs) {
  const matches = jobs.filter(job => job.name === SITE_GH.aggregateJob);
  requireSite(matches.length <= 1);
  if (matches.length === 0) return { successful: false, run, jobs };
  validateJobIdentity(matches[0], siteCohort(run), SITE_GH.aggregateJob);
  if (matches[0].status !== 'completed' || matches[0].conclusion !== 'success') return { successful: false, run, jobs };
  const cohort = siteCohort(run);
  const aggregate = matches[0];
  const steps = siteAggregateSteps();
  const inventory = aggregate.steps?.filter(step => steps.includes(step.name));
  if (inventory?.length !== steps.length || !inventory.every((step, index) => step.name === steps[index])) {
    requireSite(false);
  }
  const job = validateSuccessfulJob(uniqueNamed(jobs, SITE_GH.aggregateJob), cohort, SITE_GH.aggregateJob, steps);

  return { successful: true, run, jobs, job };
}

async function selectCurrentSiteEvidence(input, workflow, runs, producer, optional) {
  requireSite(exact(producer, SITE_GH.producerKeys) && positive(producer.runId) && positive(producer.attempt)
    && shaPattern.test(producer.sourceRevision ?? '') && SITE_GH.producerEvents.includes(producer.event));
  requireSite(SITE_GH.producerConclusions.includes(producer.conclusion));
  const matches = runs.filter(run => run.id === producer.runId);
  requireSite(matches.length === 1);
  const summary = validateSiteRun(matches[0], workflow);
  requireSite(summary.run_attempt === producer.attempt && summary.head_sha === producer.sourceRevision
    && summary.event === producer.event && summary.conclusion === producer.conclusion && completedProducer(summary));
  if (isUnsupportedSiteSource(summary.head_sha)) {
    if (optional) return { state: 'unavailable', producer };
    requireSite(false);
  }
  const pair = await readPair(input, summary, producer.attempt, workflow);
  if (pair === null) return { state: 'needs_attempt', runId: summary.id, attempt: producer.attempt };
  if (!pair.successful) {
    if (optional) return { state: 'unavailable', producer };
    requireSite(false);
  }
  requireSite(pair.run?.conclusion === producer.conclusion && completedProducer(pair.run));
  return { state: 'selected', workflow, ...pair };
}

function completedProducer(run) {
  return run.status === 'completed' && SITE_GH.producerConclusions.includes(run.conclusion);
}

export async function selectSiteIsolatedEvidence({ input, mode, requestedRun = null, producer = null, optional = false }) {
  requireSite(typeof optional === 'boolean');
  requireSite([SITE_GH.publish, SITE_GH.validate].includes(mode) && (requestedRun === null || (mode === SITE_GH.validate && /^[1-9][0-9]*$/.test(String(requestedRun)))));
  requireSite(mode === SITE_GH.publish || !optional);
  requireSite(mode === SITE_GH.publish ? producer !== null : producer === null);
  const metadata = path.join(input, SITE_GH.metadata);
  const workflow = validateSiteWorkflow(await readJson(path.join(metadata, 'workflow.json')));
  const all = flattenSiteRuns(await readJson(path.join(metadata, 'workflow_runs-pages.json')));
  if (mode === SITE_GH.publish) return selectCurrentSiteEvidence(input, workflow, all, producer, optional);
  const runs = requestedRun === null ? all : all.filter(run => String(run.id) === String(requestedRun));
  requireSite(requestedRun === null || runs.length === 1);
  runs.sort((left, right) => right.run_number - left.run_number);
  let pairs = 0;
  for (const summary of runs) {
    validateSiteRun(summary, workflow);
    for (let attempt = summary.run_attempt; attempt >= 1; attempt -= 1) {
      pairs += 1; requireSite(pairs <= SITE_GH.pairs);
      if (isUnsupportedSiteSource(summary.head_sha)) continue;
      const pair = await readPair(input, summary, attempt, workflow);
      if (pair === null) return { state: 'needs_attempt', runId: summary.id, attempt };
      if (pair.successful) return { state: 'selected', workflow, ...pair };
    }
  }
  requireSite(false);
}
