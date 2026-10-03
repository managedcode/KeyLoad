import { arch, cpus, platform, release, totalmem } from 'node:os';
import { NATIVE, positiveInteger, requireNative } from './native-serialization-contract.mjs';
import { githubJson } from './native-serialization-process.mjs';

export function executor(environment = process.env) {
  requireNative(platform() === 'linux' && environment.RUNNER_OS === 'Linux', 'executor.platform');
  requireNative(environment.GITHUB_ACTIONS === 'true' && environment.GITHUB_REPOSITORY === NATIVE.repository
    && environment.GITHUB_REF === NATIVE.ref && environment.GITHUB_WORKFLOW === NATIVE.workflow
    && environment.GITHUB_JOB === NATIVE.job, 'executor.context');
  requireNative(NATIVE.sha.test(environment.GITHUB_SHA ?? '')
    && environment.GITHUB_WORKFLOW_SHA === environment.GITHUB_SHA
    && environment.GITHUB_WORKFLOW_REF === `${NATIVE.repository}/${NATIVE.workflowPath}@${NATIVE.ref}`, 'executor.source');
  requireNative(NATIVE.positiveId.test(environment.GITHUB_RUN_ID ?? '')
    && NATIVE.positiveId.test(environment.GITHUB_RUN_ATTEMPT ?? '')
    && typeof environment.GH_TOKEN === 'string' && environment.GH_TOKEN.length > 0, 'executor.identity');
  return { sourceRevision: environment.GITHUB_SHA, workflowRevision: environment.GITHUB_WORKFLOW_SHA,
    repository: NATIVE.repository, ref: NATIVE.ref, workflow: NATIVE.workflow, workflowPath: NATIVE.workflowPath,
    runId: environment.GITHUB_RUN_ID, attempt: environment.GITHUB_RUN_ATTEMPT, job: NATIVE.job,
    jobName: NATIVE.jobName, event: environment.GITHUB_EVENT_NAME };
}

function validateRun(run, identity) {
  requireNative(String(run.id) === identity.runId && String(run.run_attempt) === identity.attempt
    && run.head_sha === identity.sourceRevision && run.head_branch === 'main'
    && run.repository?.full_name === NATIVE.repository && run.name === NATIVE.workflow
    && run.head_repository?.full_name === NATIVE.repository && run.head_repository?.id === run.repository?.id
    && run.path === NATIVE.workflowPath && run.event === 'workflow_dispatch'
    && identity.event === 'workflow_dispatch' && run.status === 'in_progress', 'github.run');
}

export async function captureGitHub(identity, signal, saveResponse) {
  const route = `repos/${NATIVE.repository}/actions/runs/${identity.runId}/attempts/${identity.attempt}`;
  const run = await githubJson(route, signal);
  await saveResponse?.('run', run, route);
  validateRun(run, identity);
  const pages = [];
  const jobs = [];
  let total;
  for (let page = 1; page <= 20; page++) {
    const pageRoute = `${route}/jobs?per_page=100&page=${page}`;
    const value = await githubJson(pageRoute, signal);
    await saveResponse?.(`jobs-${String(page).padStart(2, '0')}`, value, pageRoute);
    requireNative(positiveInteger(value.total_count) && value.total_count <= 2000
      && Array.isArray(value.jobs) && value.jobs.length <= 100, 'github.jobs');
    total ??= value.total_count;
    requireNative(value.total_count === total, 'github.inventoryChanged');
    pages.push(value);
    jobs.push(...value.jobs);
    if (jobs.length >= value.total_count) break;
    requireNative(value.jobs.length === 100 && page < 20, 'github.pagination');
  }
  requireNative(jobs.length === pages[0].total_count && new Set(jobs.map(job => job.id)).size === jobs.length, 'github.inventory');
  const selected = jobs.filter(job => job.name === NATIVE.jobName);
  requireNative(selected.length === 1, 'github.job');
  const job = selected[0];
  requireNative(positiveInteger(job.id) && String(job.run_id) === identity.runId
    && (!Object.hasOwn(job, 'run_attempt') || job.run_attempt === Number(identity.attempt))
    && job.head_sha === identity.sourceRevision
    && job.status === 'in_progress' && job.conclusion === null
    && [ `https://github.com/${NATIVE.repository}/actions/runs/${identity.runId}/job/${job.id}`,
      `https://github.com/${NATIVE.repository}/runs/${identity.runId}/jobs/${job.id}` ].includes(job.html_url)
    && Array.isArray(job.labels) && job.labels.includes('ubuntu-latest'), 'github.jobIdentity');
  return { run, job, pages };
}

export function hostFacts() {
  return { platform: platform(), release: release(), architecture: arch(), logicalProcessors: cpus().length,
    processorModels: [...new Set(cpus().map(cpu => cpu.model))], totalMemoryBytes: totalmem() };
}

export function requiredStepsReady(job, measured = false) {
  const names = ['Build diagnostic tests and generated benchmark host',
    'Test native diagnostic contracts and generated consumers', 'Test diagnostic contracts without hardware intrinsics'];
  if (measured) names.push('Record source packages runtime and execution identity',
    'Measure all native and historical JSON diagnostic cases');
  requireNative(Array.isArray(job?.steps), 'github.steps');
  let ready = true;
  for (const name of names) {
    const steps = job.steps.filter(step => step?.name === name);
    requireNative(steps.length === 1, 'github.requiredStep');
    const completed = steps[0].status === 'completed' && steps[0].conclusion === 'success';
    const pending = ['queued', 'in_progress', 'pending'].includes(steps[0].status) && steps[0].conclusion === null;
    requireNative(completed || pending, 'github.requiredStep');
    ready &&= completed;
  }
  return ready;
}

export function requireCompletedSteps(job, measured = false) {
  requireNative(requiredStepsReady(job, measured), 'github.requiredStep');
}
