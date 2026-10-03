import { validateCohort } from './aggregate-contracts.mjs';
import { GH, canonicalJobUrl, digestPattern, hashPattern, positive, requireGitHub, timestamp } from './isolated-github-contract.mjs';

export function validateWorkflowRun(workflow, run, cohort) {
  validateCohort(cohort, cohort.profile);
  requireGitHub(workflow?.path === GH.workflowPath && workflow.name === GH.workflow && workflow.state === 'active'
    && positive(workflow.id) && run?.workflow_id === workflow.id && run.id === cohort.runId
    && run.run_attempt === cohort.attempt && run.head_sha === cohort.sourceRevision && run.head_branch === 'main'
    && run.name === GH.workflow && run.path === GH.workflowPath && run.repository?.full_name === GH.repository
    && run.head_repository?.full_name === GH.repository && positive(run.repository.id)
    && run.head_repository.id === run.repository.id);
  timestamp(run.run_started_at);
  return run;
}

export function flattenPages(pages, key) {
  requireGitHub(['jobs', 'artifacts'].includes(key) && Array.isArray(pages) && pages.length > 0 && pages.length <= GH.pages);
  const total = pages[0]?.total_count;
  requireGitHub(Number.isSafeInteger(total) && total >= 0 && total <= GH.items
    && pages.length === Math.max(1, Math.ceil(total / GH.pageSize)));
  const items = [];
  const ids = new Set();
  for (const [index, page] of pages.entries()) {
    requireGitHub(page.total_count === total && Array.isArray(page[key])
      && page[key].length === Math.min(GH.pageSize, total - index * GH.pageSize));
    for (const item of page[key]) {
      requireGitHub(positive(item?.id) && !ids.has(item.id));
      ids.add(item.id);
      items.push(item);
    }
  }
  return items;
}

export function validateJobIdentity(job, cohort, name) {
  requireGitHub(job !== undefined && positive(job.id) && job.name === name && job.run_id === cohort.runId
    && (!Object.hasOwn(job, 'run_attempt') || job.run_attempt === cohort.attempt) && job.head_sha === cohort.sourceRevision);
  const canonical = canonicalJobUrl(cohort, job.id);
  const legacy = `https://github.com/${cohort.repository}/runs/${cohort.runId}/jobs/${job.id}`;
  requireGitHub(job.html_url === canonical || job.html_url === legacy);
  timestamp(job.started_at);
  return job;
}

function validateCompletedJob(job, cohort, name, requiredSteps, conclusions) {
  validateJobIdentity(job, cohort, name);
  requireGitHub(job.status === 'completed' && conclusions.includes(job.conclusion)
    && timestamp(job.completed_at) >= timestamp(job.started_at) && Array.isArray(job.steps));
  const numbers = new Set();
  for (const step of job.steps) {
    requireGitHub(positive(step?.number) && !numbers.has(step.number));
    numbers.add(step.number);
  }
  for (const name of requiredSteps) {
    const matched = job.steps.filter(step => step.name === name);
    requireGitHub(matched.length === 1 && matched[0].status === 'completed' && matched[0].conclusion === (name === GH.workerSteps[0] ? job.conclusion : 'success'));
  }
  return job;
}

export function validateSuccessfulJob(job, cohort, name, requiredSteps) {
  return validateCompletedJob(job, cohort, name, requiredSteps, ['success']);
}

export function validateWorkerJob(job, cohort, name) {
  return validateCompletedJob(job, cohort, name, GH.workerSteps, ['success', 'failure']);
}

export function validateArtifact(artifact, run, job, name, maximumBytes) {
  const source = artifact?.workflow_run;
  requireGitHub(positive(artifact?.id) && artifact.name === name && positive(artifact.size_in_bytes)
    && artifact.size_in_bytes <= maximumBytes && digestPattern.test(artifact.digest ?? '') && artifact.expired === false
    && source?.id === run.id && source.head_sha === run.head_sha && source.head_branch === run.head_branch
    && source.repository_id === run.repository.id && source.head_repository_id === run.head_repository.id);
  const created = timestamp(artifact.created_at);
  requireGitHub(created >= timestamp(job.started_at) && created <= timestamp(job.completed_at));
  return artifact;
}

export function uniqueNamed(items, name) {
  const matches = items.filter(item => item.name === name);
  requireGitHub(matches.length === 1);
  return matches[0];
}

export function projectJob(job, cohort, steps) {
  return { id: job.id, name: job.name, url: canonicalJobUrl(cohort, job.id), conclusion: job.conclusion,
    steps: steps.map(name => {
      const matched = job.steps.filter(step => step.name === name);
      requireGitHub(matched.length === 1 && matched[0].status === 'completed');
      return { name, conclusion: matched[0].conclusion };
    }) };
}

export function projectArtifact(artifact) {
  return { id: artifact.id, name: artifact.name, sizeInBytes: artifact.size_in_bytes,
    digest: artifact.digest, expired: artifact.expired };
}

export function projectWorkerProof(job, artifact, cell, cohort, workerSha256) {
  requireGitHub(hashPattern.test(workerSha256));
  return { id: cell.id, job: projectJob(job, cohort, GH.workerSteps), artifact: projectArtifact(artifact), workerSha256 };
}
