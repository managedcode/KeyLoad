// Shared bounded evidence primitives. No retired per-cell producer schema is admitted here.
import { SESSION, validateSessionCohort } from './session-contract.mjs';
export const AGGREGATE = Object.freeze({ encoding: 'utf8', hash: 'sha256',
  digest: /^[a-f0-9]{64}$/, zipDigest: /^sha256:[a-f0-9]{64}$/,
  errors: Object.freeze({ input: 'E_AGGREGATE_INPUT', output: 'E_AGGREGATE_OUTPUT', cohort: 'E_AGGREGATE_COHORT' }) });
export function reject(code) {
  const error = new Error(SESSION.failure);
  error.code = code;
  throw error;
}
export function requireValue(condition, code) { if (!condition) reject(code); }
export const exactKeys = (value, keys) => value !== null && typeof value === 'object' && !Array.isArray(value)
  && Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key));
export function validateCohort(value, profile) {
  requireValue(profile === SESSION.profile, AGGREGATE.errors.cohort);
  return validateSessionCohort(value);
}
