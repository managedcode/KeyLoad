// Current Website receipt authority is the session cohort only.
export { validateSessionSiteReceipt as validateSiteIsolatedReceipt } from './site-session-receipt.mjs';
export const sameSiteIdentity = (left, right) => JSON.stringify(left) === JSON.stringify(right);
