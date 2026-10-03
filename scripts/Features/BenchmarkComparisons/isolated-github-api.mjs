import path from 'node:path';
import { requestNative } from './isolated-github-transport.mjs';
import { readJson, writeJson } from './isolated-github-files.mjs';
import { GH, requireGitHub } from './isolated-github-contract.mjs';
import { flattenPages, validateWorkflowRun } from './isolated-github-validation.mjs';

export async function captureApi(endpoint, target, paginated, context) {
  requireGitHub(paginated === false);
  await requestNative(endpoint, target, GH.metadataBytes, GH.metadataTimeoutMs, context);
  return readJson(target);
}

async function capturePages(route, directory, key, context) {
  const pages = [];
  let count = 1;
  for (let page = 1; page <= count; page += 1) {
    const value = await captureApi(`${route}?per_page=${GH.pageSize}&page=${page}`,
      path.join(directory, `${key}-page-${String(page).padStart(2, '0')}.json`), false, context);
    requireGitHub(Number.isSafeInteger(value.total_count) && value.total_count >= 0 && value.total_count <= GH.items);
    count = Math.max(1, Math.ceil(value.total_count / GH.pageSize));
    requireGitHub(count <= GH.pages && (pages.length === 0 || value.total_count === pages[0].total_count));
    pages.push(value);
  }
  flattenPages(pages, key);
  await writeJson(path.join(directory, `${key}-pages.json`), pages);
  return flattenPages(pages, key);
}

export async function captureRun(directory, context, includeArtifacts) {
  const { runId, attempt } = context.cohort;
  const workflow = await captureApi(`${GH.api}/workflows/benchmarks.yml`, path.join(directory, 'workflow.json'), false, context);
  const route = `${GH.api}/runs/${runId}/attempts/${attempt}`;
  const run = await captureApi(route, path.join(directory, 'run-attempt.json'), false, context);
  validateWorkflowRun(workflow, run, context.cohort);
  const jobs = await capturePages(`${route}/jobs`, directory, 'jobs', context);
  if (!includeArtifacts) return { run, jobs };
  const artifacts = await capturePages(`${GH.api}/runs/${runId}/artifacts`, directory, 'artifacts', context);
  return { run, jobs, artifacts };
}

export async function captureCurrentJobPages(directory, context, name) {
  const { runId, attempt } = context.cohort;
  const route = `${GH.api}/runs/${runId}/attempts/${attempt}/jobs`;
  const pages = [];
  const ids = new Set();
  let count = 1;
  for (let page = 1; page <= count; page += 1) {
    const value = await captureApi(`${route}?per_page=${GH.pageSize}&page=${page}`,
      path.join(directory, `jobs-page-${String(page).padStart(2, '0')}.json`), false, context);
    requireGitHub(Number.isSafeInteger(value.total_count) && value.total_count >= 0 && value.total_count <= GH.items);
    count = Math.max(1, Math.ceil(value.total_count / GH.pageSize));
    requireGitHub(count <= GH.pages && Array.isArray(value.jobs)
      && value.jobs.length === Math.min(GH.pageSize, value.total_count - (page - 1) * GH.pageSize)
      && (pages.length === 0 || value.total_count === pages[0].total_count));
    for (const job of value.jobs) {
      requireGitHub(Number.isSafeInteger(job.id) && job.id > 0 && !ids.has(job.id));
      ids.add(job.id);
    }
    pages.push(value);
    const matches = pages.flatMap(item => item.jobs).filter(job => job.name === name);
    requireGitHub(matches.length <= 1);
    if (matches.length === 1) {
      await writeJson(path.join(directory, 'jobs-pages.json'), pages);
      return matches[0];
    }
  }
  requireGitHub(false);
}

export const downloadArtifact = (artifact, target, maximumBytes, context) => requestNative(
  `${GH.api}/artifacts/${artifact.id}/zip`, target, maximumBytes, GH.downloadTimeoutMs, context);
