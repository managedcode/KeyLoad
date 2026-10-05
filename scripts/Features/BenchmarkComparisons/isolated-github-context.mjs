import { createRunContext } from './image-inputs.mjs';
import { createIsolatedPlan, validateIsolatedPlan } from './isolated-plan.mjs';
import { validateCohort } from './aggregate-contracts.mjs';
import { GH, isolatedEvidenceJobName, isolatedJobName, positive, requireGitHub } from './isolated-github-contract.mjs';
import { createScaledPlans } from './scaled-isolated-plan.mjs';
import { createVectorPlans } from './vector-isolated-plan.mjs';

export function createGitHubContext(environment, platform) {
  const native = createRunContext(environment, platform);
  requireGitHub(typeof environment.GH_TOKEN === 'string' && environment.GH_TOKEN.length > 0
    && environment.GITHUB_WORKFLOW === GH.workflow && native.repository === GH.repository && native.ref === 'refs/heads/main');
  const plan = validateIsolatedPlan(createIsolatedPlan());
  const scaledPlans = createScaledPlans();
  const vectorPlans = createVectorPlans();
  const cohort = { sourceRevision: native.sourceSha, runId: Number(native.runId), attempt: Number(native.runAttempt),
    repository: native.repository, ref: native.ref, workflow: GH.workflow, profile: plan.profile };
  requireGitHub(positive(cohort.runId) && positive(cohort.attempt));
  validateCohort(cohort, plan.profile);
  return { native, cohort, plan, scaledPlans, vectorPlans };
}

export function requireCurrentJobName(name, context) {
  requireGitHub(typeof name === 'string');
  if (name === GH.imageJob) return name;
  const cells = [...context.plan.cells, ...context.scaledPlans.flatMap(profile => profile.cells),
    ...context.vectorPlans.flatMap(profile => profile.cells)];
  requireGitHub(cells.some(cell => isolatedEvidenceJobName(cell) === name || isolatedJobName(cell, true) === name));
  return name;
}

export function contextForProfile(context, profile) {
  const cohort = { ...context.cohort, profile };
  validateCohort(cohort, profile);
  return { ...context, cohort };
}

export function parseCollectArguments(argv) {
  requireGitHub(Array.isArray(argv) && argv.length === 4);
  const entries = new Map();
  for (const argument of argv) {
    const matched = /^--(plan|scale-plan|vector-plan|input)=(.+)$/.exec(argument);
    requireGitHub(matched !== null && !entries.has(matched[1]));
    entries.set(matched[1], matched[2]);
  }
  requireGitHub(entries.size === 4);
  return { plan: entries.get('plan'), scalePlan: entries.get('scale-plan'), vectorPlan: entries.get('vector-plan'), input: entries.get('input') };
}
