import { metrics } from './measurements.mjs';
import { SESSION, SESSION_TARGETS, requireSession, sessionRows, sessionUnavailableTopology } from '../../../scripts/Features/BenchmarkComparisons/session-contract.mjs';

function verifiedExecution(worker) {
  const profile = worker.session.targetProfile;
  return worker.qualification.status !== 'failed' && profile !== null
    && [profile.version,profile.topology,profile.cluster.state].every(value => !/unverified|unobserved|unknown|not recorded/i.test(value));
}
const topologyUnavailable = worker => worker.session.images.server === null && sessionUnavailableTopology(worker.session);
const notRecorded = 'Not recorded in session manifest';
function disposition(worker, report) {
  if (report.status === 'notQualified' || report.status === 'measured' && !verifiedExecution(worker)) return 'failed';
  if (report.status === 'measured' && !worker.resourceQualified) return 'resourceIncomplete';
  return report.status === 'unavailable' ? topologyUnavailable(worker) ? 'unsupportedTopology' : 'unsupported' : report.status;
}
function viewWorker(worker, report) {
  const profile = worker.session.targetProfile;
  const runtime = worker.session.runtime;
  const target = profile ?? { name: worker.target, version: notRecorded, image: notRecorded,
    topology: topologyUnavailable(worker) ? 'Execution topology unavailable' : 'Execution profile not observed for this workload', cluster: { dataCopies: notRecorded, observations: [] },
    writeAcknowledgement: notRecorded, readContract: notRecorded, transport: notRecorded, authorization: notRecorded };
  const status = disposition(worker, report);
  return { ...worker, rawSha256: worker.sessionSha256, disposition: status,
    reason: status === 'resourceIncomplete' ? 'Server resource telemetry is incomplete. Recorded operation measurements remain available.'
      : worker.qualification.status === 'failed' ? worker.session.reason : report.detail,
    report: { hostOs: runtime.operatingSystem, architecture: runtime.architecture, logicalProcessors: runtime.logicalProcessors,
      runtime: runtime.framework, storage: runtime.storage, client: worker.session.processes.client,
      serverImage: worker.session.images.server ?? notRecorded, targets: [target] } };
}
export function selectedIsolatedRows(projection, scenario, nodeCount, repetition, metric, target) {
  const [caseId, laneId] = scenario.split('/');
  requireSession(SESSION.caseIds.includes(caseId) && (laneId === undefined
    || caseId === 'mixed' && ['create', 'delete', 'update', 'read', 'search'].includes(laneId)
    || caseId === 'stream-mixed' && ['stream-append', 'stream-read'].includes(laneId)) && SESSION.nodes.includes(nodeCount) && Object.hasOwn(metrics, metric)
    && (target === 'all' || SESSION_TARGETS.some(item => item.name === target)) && repetition === 'session');
  return sessionRows().filter(row => row.nodeCount === nodeCount && (target === 'all' || row.target === target))
    .map(row => {
      const worker = projection.workers.find(candidate => candidate.id === row.id);
      if (worker === undefined) return { id: row.id, name: row.target, status: 'missing', value: null,
        min: null, max: null, attempts: null, successes: null, failures: null, throughput: null,
        p50: null, p95: null, p99: null, detail: 'No verified original result for this session.', nodeCount, worker: null };
      const report = worker.reports.find(item => item.caseId === caseId);
      requireSession(report !== undefined);
      const selected = laneId === undefined ? report.lanes.length === 1 ? report.lanes[0] : null
        : report.lanes.find(lane => lane.id === laneId);
      const measurement = report.status === 'measured' && verifiedExecution(worker) ? selected?.measurement ?? null : null;
      // Concurrent mixed lanes are separate streams, never averaged into a fictitious request distribution.
      const value = report.status !== 'measured' || !verifiedExecution(worker) ? null : metric === 'duration'
        ? selected?.measurement.elapsedSeconds ?? report.elapsedSeconds : measurement ? metrics[metric].read(measurement) : null;
      return { id: worker.id, name: worker.target, status: disposition(worker, report),
        value: Number.isFinite(value) ? value : null, min: Number.isFinite(value) ? value : null,
        max: Number.isFinite(value) ? value : null, attempts: measurement?.attempts ?? null,
        successes: measurement?.successes ?? null, failures: measurement?.failures ?? null,
        throughput: measurement?.usefulOperationsPerSecond ?? null, p50: measurement?.latency.p50Ms ?? null,
        p95: measurement?.latency.p95Ms ?? null, p99: measurement?.latency.p99Ms ?? null,
        detail: report.detail, nodeCount, worker: viewWorker(worker, report) };
    }).sort((left, right) => left.value === null ? right.value === null ? 0 : 1 : right.value === null ? -1
      : metrics[metric].higher ? right.value - left.value : left.value - right.value);
}
