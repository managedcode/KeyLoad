import { createRunContext } from './image-inputs.mjs';
import { createIsolatedPlan, validateIsolatedPlan } from './isolated-plan.mjs';
import { validateCohort } from './aggregate-contracts.mjs';
import { GH, positive, requireGitHub } from './isolated-github-contract.mjs';

export function createGitHubContext(environment, platform) {
  const native = createRunContext(environment, platform);
  requireGitHub(typeof environment.GH_TOKEN === 'string' && environment.GH_TOKEN.length > 0
    && environment.GITHUB_WORKFLOW === GH.workflow && native.repository === GH.repository && native.ref === 'refs/heads/main');
  const plan = validateIsolatedPlan(createIsolatedPlan());
  const cohort = { sourceRevision: native.sourceSha, runId: Number(native.runId), attempt: Number(native.runAttempt),
    repository: native.repository, ref: native.ref, workflow: GH.workflow, profile: plan.profile };
  requireGitHub(positive(cohort.runId) && positive(cohort.attempt));
  validateCohort(cohort, plan.profile);
  return { native, cohort, plan };
}

export function requireCurrentJobName(name, plan) {
  requireGitHub(typeof name === 'string');
  if (name === GH.imageJob) return name;
  const prefix = name.startsWith(GH.casePrefix) ? GH.casePrefix : GH.preflightPrefix;
  requireGitHub(name.startsWith(prefix) && plan.cells.some(cell => prefix + cell.id === name));
  return name;
}

export function parseCollectArguments(argv) {
  requireGitHub(Array.isArray(argv) && argv.length === 2);
  const entries = new Map();
  for (const argument of argv) {
    const matched = /^--(plan|input)=(.+)$/.exec(argument);
    requireGitHub(matched !== null && !entries.has(matched[1]));
    entries.set(matched[1], matched[2]);
  }
  requireGitHub(entries.size === 2);
  return { plan: entries.get('plan'), input: entries.get('input') };
}
