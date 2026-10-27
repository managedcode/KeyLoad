import { join } from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { readBytes, parseBytes } from '../../../scripts/Features/BenchmarkComparisons/aggregate-files.mjs';
import { SESSION, requireSession, sessionRows } from '../../../scripts/Features/BenchmarkComparisons/session-contract.mjs';
import { validateSessionAggregate } from '../../../scripts/Features/BenchmarkComparisons/session-aggregate-contract.mjs';
import { readOriginalSession } from '../../../scripts/Features/BenchmarkComparisons/session-aggregate.mjs';

// Every published measurement is rederived from hash-bound original session case files.
export async function produceIsolatedProjection({ input }) {
  const aggregate = validateSessionAggregate(parseBytes(await readBytes(join(input, 'aggregate.json'), SESSION.aggregateBytes)));
  for (const worker of aggregate.workers) {
    const row = sessionRows().find(candidate => candidate.id === worker.id);
    const original = await readOriginalSession(join(input, 'workers', row.id), row, aggregate.cohort, worker);
    const compact = original.reports.map(report => ({ ...report,
      lanes: report.lanes.map(({ samples, ...lane }) => lane) }));
    requireSession(isDeepStrictEqual(original.session, worker.session) && isDeepStrictEqual(compact, worker.reports)
      && isDeepStrictEqual(original.execution, worker.execution) && isDeepStrictEqual(original.qualification,worker.qualification) && original.stateSha256 === worker.stateSha256
      && (original.resources?.observations.qualified ?? false) === worker.resourceQualified);
  }
  return { ...aggregate, profile: SESSION.profile,
    options: { operations: SESSION.operations, concurrency: SESSION.concurrency, payloadBytes: 'not recorded' }, datasetSha256: null };
}
