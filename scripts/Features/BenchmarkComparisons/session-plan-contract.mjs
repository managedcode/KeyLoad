import { SESSION, requireSession, sessionRows } from './session-contract.mjs';
export function createSessionPlan() {
  return { schemaVersion: SESSION.schema, profile: SESSION.profile, initialRecords: SESSION.records,
    minimumOperations: SESSION.operations, warmup: SESSION.warmup, caseIds: [...SESSION.caseIds], workers: sessionRows() };
}
export function validateSessionPlan(value) {
  requireSession(sameValue(value, createSessionPlan()));
  return value;
}

function sameValue(left, right) {
  if (left === right) return true;
  if (Array.isArray(right)) return Array.isArray(left) && left.length === right.length
    && right.every((item, index) => sameValue(left[index], item));
  if (right === null || typeof right !== 'object' || left === null || typeof left !== 'object' || Array.isArray(left)) return false;
  return Object.keys(left).length === Object.keys(right).length
    && Object.keys(right).every(key => Object.hasOwn(left, key) && sameValue(left[key], right[key]));
}
