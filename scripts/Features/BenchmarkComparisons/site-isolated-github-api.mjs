import path from 'node:path';
import { captureApi } from './isolated-github-api.mjs';
import { GH } from './isolated-github-contract.mjs';
import { writeJson } from './isolated-github-files.mjs';
import { flattenPages } from './isolated-github-validation.mjs';
import { flattenSiteRuns } from './site-isolated-github-runs.mjs';
import { requireSite } from './site-isolated-github-contract.mjs';

export async function captureSitePages(route, directory, key, context) {
  const pages = [];
  let count = 1;
  for (let page = 1; page <= count; page += 1) {
    const separator = route.includes('?') ? '&' : '?';
    const value = await captureApi(`${route}${separator}per_page=${GH.pageSize}&page=${page}`,
      path.join(directory, `${key}-page-${String(page).padStart(2, '0')}.json`), false, context);
    requireSite(Number.isSafeInteger(value.total_count) && value.total_count >= 0 && value.total_count <= GH.items);
    count = Math.max(1, Math.ceil(value.total_count / GH.pageSize));
    requireSite(count <= GH.pages && (pages.length === 0 || value.total_count === pages[0].total_count));
    pages.push(value);
  }
  if (key === 'workflow_runs') flattenSiteRuns(pages);
  else flattenPages(pages, key);
  await writeJson(path.join(directory, `${key}-pages.json`), pages);
  return pages;
}
