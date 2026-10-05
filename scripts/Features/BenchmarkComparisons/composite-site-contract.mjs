import { createIsolatedPlan } from './isolated-plan.mjs';
import { createScaledPlans } from './scaled-isolated-plan.mjs';
import { createVectorPlans } from './vector-isolated-plan.mjs';

export const compositeSitePlans = () => [createIsolatedPlan(), ...createScaledPlans(), ...createVectorPlans()];
export const familyRoot = profile => profile === 'intensive-1k-c16' ? ''
  : `${profile.startsWith('vector-') ? 'vector' : 'scaled'}/${profile}/`;
export const compositeSiteCells = () => compositeSitePlans().flatMap(plan => plan.cells);
export const compositeSuiteFiles = () => ['cohort-receipt.json', ...compositeSitePlans().flatMap(plan => [
  `${familyRoot(plan.profile)}aggregate.json`, ...plan.cells.flatMap(cell => [
    `${familyRoot(plan.profile)}workers/${cell.id}/worker.json`,
    ...(familyRoot(plan.profile) ? [`${familyRoot(plan.profile)}workers/${cell.id}/server-resource-evidence.json`] : [])])])];
export const compositeProviderFiles = () => ['proof.json', 'image-proof.json', 'images/image-bundle.json',
  'images/image-receipt.json', 'images/server-manifest.json', 'images/comparisons-manifest.json', 'composite-plan.json',
  ...compositeSitePlans().map(plan => `plans/${plan.profile}.json`),
  ...compositeSitePlans().slice(1).map(plan => `${familyRoot(plan.profile)}proof.json`)];
