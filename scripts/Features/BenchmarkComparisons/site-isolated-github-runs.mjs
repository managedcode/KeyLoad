import { access } from 'node:fs/promises';
import path from 'node:path';
import { GH, positive } from './isolated-github-contract.mjs';
import { readJson } from './isolated-github-files.mjs';
import { flattenPages, uniqueNamed, validateJobIdentity, validateSuccessfulJob } from './isolated-github-validation.mjs';
import { createIsolatedPlan } from './isolated-plan.mjs';
import { SITE_GH, requireSite } from './site-isolated-github-contract.mjs';

export function validateSiteWorkflow(workflow) {
  requireSite(workflow?.path === GH.workflowPath && workflow.name === GH.workflow && workflow.state === 'active' && positive(workflow.id));
  return workflow;
}

export function validateSiteRun(run, workflow) {
  requireSite(positive(run?.id) && positive(run.run_number) && positive(run.run_attempt) && run.workflow_id === workflow.id
    && run.path === GH.workflowPath && run.name === GH.workflow && run.event === 'push' && run.head_branch === 'main'
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

export const siteCohort = run => ({ sourceRevision: run.head_sha, runId: run.id, attempt: run.run_attempt,
  repository: SITE_GH.repository, ref: 'refs/heads/main', workflow: GH.workflow, profile: createIsolatedPlan().profile });

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
  requireSite(run.id === summary.id && run.run_number === summary.run_number && run.head_sha === summary.head_sha && run.run_attempt === attempt);
  const jobs = flattenPages(await readJson(path.join(root, names[1])), 'jobs');
  const matches = jobs.filter(job => job.name === SITE_GH.aggregateJob);
  requireSite(matches.length <= 1);
  if (matches.length === 0) return { successful: false };
  validateJobIdentity(matches[0], siteCohort(run), SITE_GH.aggregateJob);
  if (matches[0].status !== 'completed' || matches[0].conclusion !== 'success') return { successful: false };
  return { successful: true, run, jobs,
    job: validateSuccessfulJob(uniqueNamed(jobs, SITE_GH.aggregateJob), siteCohort(run), SITE_GH.aggregateJob, SITE_GH.steps) };
}

export async function selectSiteIsolatedEvidence({ input, mode, requestedRun = null }) {
  requireSite([SITE_GH.publish, SITE_GH.validate].includes(mode) && (requestedRun === null || (mode === SITE_GH.validate && /^[1-9][0-9]*$/.test(String(requestedRun)))));
  const metadata = path.join(input, SITE_GH.metadata);
  const workflow = validateSiteWorkflow(await readJson(path.join(metadata, 'workflow.json')));
  const all = flattenSiteRuns(await readJson(path.join(metadata, 'workflow_runs-pages.json')));
  const runs = requestedRun === null ? all : all.filter(run => String(run.id) === String(requestedRun));
  requireSite(requestedRun === null || runs.length === 1);
  runs.sort((left, right) => right.run_number - left.run_number);
  let pairs = 0;
  for (const summary of runs) {
    validateSiteRun(summary, workflow);
    for (let attempt = summary.run_attempt; attempt >= 1; attempt -= 1) {
      pairs += 1; requireSite(pairs <= SITE_GH.pairs);
      const pair = await readPair(input, summary, attempt, workflow);
      if (pair === null) return { state: 'needs_attempt', runId: summary.id, attempt };
      if (pair.successful) return { state: 'selected', workflow, ...pair };
    }
  }
  requireSite(false);
}
