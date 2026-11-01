import { NODES, TYPES } from './results-contract.mjs';

const QUERY_CASES = Object.freeze(['range-query', 'range-select', 'traverse', 'exact-top10',
  'term-query', 'and-query', 'aggregate', 'windows', 'group-by', 'filtered-aggregate']);
export const SUMMARY_COLUMNS = Object.freeze([
  { id: 'load', label: 'Load', kind: 'write', cases: ['load'] },
  { id: 'write', label: 'Write', kind: 'write', cases: ['load', 'update', 'set', 'append', 'cycle'] },
  { id: 'read', label: 'Read', kind: 'read', cases: ['point-read', 'get', 'pk-select', 'neighbors', 'range-read', 'read',
    ...QUERY_CASES, 'cycle'] },
  { id: 'search', label: 'Search', kind: 'query', cases: QUERY_CASES },
  { id: 'delete', label: 'Delete', kind: 'write', cases: ['delete'] },
  { id: 'mixed', label: 'Mixed', kind: 'mixed', cases: ['mixed', 'cycle'] },
]);

function summarize(results, database, column, nodes) {
  const sources = results.filter(result => result.database === database && result.nodes === nodes).flatMap(result => {
    const type = TYPES.find(type => type.id === result.type);
    return type.cases.filter(item => column.cases.includes(item.id)).map(item => ({
      key: `${type.id}|${item.id}`, label: `${type.label} / ${item.label}`, result,
      entry: result.cases.find(entry => entry.id === item.id),
    }));
  });
  const base = { nodes, summary: true, caseCount: sources.length, coverage: sources.map(source => source.key).sort().join(',') };
  const failed = sources.filter(source => source.entry?.status === 'failed' || (!source.entry && source.result.error !== null));
  if (failed.length > 0) {
    const error = failed.map(source => `${source.label}: ${source.entry?.error ?? source.result.error}`).join('\n');
    return { ...base, status: 'failed', reason: `${failed.length} of ${sources.length} cases failed: ${error.split(/\r?\n/)[0]}`, error };
  }
  const missing = sources.filter(source => !source.entry);
  if (sources.length === 0 || missing.length > 0) return { ...base, status: 'missing',
    reason: sources.length === 0 ? 'no recorded cases' : `${missing.length} of ${sources.length} cases not run` };
  const operations = sources.reduce((sum, source) => sum + source.entry.operations, 0);
  const seconds = sources.reduce((sum, source) => sum + source.entry.seconds, 0);
  if (seconds <= 0) return { ...base, status: 'missing', reason: 'no recorded elapsed time' };
  const queue = sources.some(source => source.key === 'queue|cycle');
  const basis = !queue || !['write', 'read'].includes(column.id) ? null
    : sources.length === 1 ? 'Full queue cycle' : 'Includes queue cycle';
  return { ...base, status: 'passed', value: operations / seconds, operations, seconds, basis };
}

// Rates describe the recorded workload: completed operations divided by its combined measured duration.
export function summaryModel(results) {
  const databases = [...new Set(TYPES.flatMap(type => type.databases))];
  const byKey = new Map(databases.flatMap(database => SUMMARY_COLUMNS.flatMap(column => NODES.map(nodes =>
    [`${database}|${column.id}|${nodes}`, summarize(results, database, column, nodes)]))));
  const line = (database, column, nodes) => byKey.get(`${database}|${column.id}|${nodes}`);
  const result = (database, nodes) => results.find(result => result.database === database && result.nodes === nodes);
  const best = new Map(SUMMARY_COLUMNS.map(column => [column.id, Math.max(0, ...databases.flatMap(database => NODES.map(nodes =>
    line(database, column, nodes))).filter(line => line.status === 'passed').map(line => line.value))]));
  const supports = (database, column) => TYPES.some(type => type.databases.includes(database) &&
    type.cases.some(item => column.cases.includes(item.id)));
  return { summary: true, type: { id: 'all', label: 'All types', databases, cases: SUMMARY_COLUMNS }, results, line, result, best, supports,
    nodes: database => NODES.filter(nodes => nodes === 1 || result(database, nodes) !== undefined) };
}
