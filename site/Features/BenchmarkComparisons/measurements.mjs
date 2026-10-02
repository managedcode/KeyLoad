import { CONFIG, TEXT } from './contracts.mjs';

const SCENARIO_DESCRIPTIONS = Object.freeze({
  PointRead: Object.freeze(['Point read', 'Primary-key reads returning the complete document.']),
  DocumentWrite: Object.freeze(['Document write', 'Unique document creates, with readback validation outside the timer.']),
  VectorExact: Object.freeze(['Exact vector', 'Exhaustive cosine top-K with the complete document projection, checked against an independent oracle.']),
  QueueCycle: Object.freeze(['Queue cycle', 'Enqueue → receive → ACK. Useful throughput counts unique, verified completed messages.']),
  GraphNeighbors: Object.freeze(['Graph neighbors', 'One-hop outgoing neighbors, returned as sorted distinct vertex IDs.']),
  GraphTraverse: Object.freeze(['Graph traversal', 'Bounded directed reachability with cycles and disconnected components, checked against breadth-first traversal.']),
});

export const scenarios = SCENARIO_DESCRIPTIONS;

export const colors = Object.freeze({
  KeyLoad: '#2f55e4',
  'PostgreSQL + pgvector': '#1baf7a',
  Qdrant: '#e87ba4',
  RabbitMQ: '#eb6834',
  Redis: '#e34948',
  Neo4j: '#eda100',
});

const VALUE_FIELDS = Object.freeze({ throughput: 'usefulOperationsPerSecond', p50: 'p50Ms', p95: 'p95Ms', p99: 'p99Ms' });
const REPORT_FIELDS = Object.freeze({ targets: 'targets', cases: 'cases' });
const CASE_FIELDS = Object.freeze({ target: 'target', scenario: 'scenario', repetition: 'repetition', measurement: 'measurement',
  detail: 'detail', status: 'status' });
const TARGET_FIELDS = Object.freeze({ name: 'name' });
const MEASUREMENT_FIELDS = Object.freeze({ latency: 'latency', enqueue: 'enqueue', receive: 'receive', ack: 'ack',
  clientResources: 'clientResources', cpuSeconds: 'cpuSeconds', allocatedBytes: 'allocatedBytes', peakRss: 'peakObservedWorkingSetBytes',
  attempts: 'attempts', failures: 'failures', successes: 'successes' });
const FACTORS = Object.freeze({ percent: CONFIG.units.percent, milliseconds: CONFIG.units.millisecondsPerSecond,
  kibibytes: CONFIG.units.bytesPerKiB, mebibytes: CONFIG.units.bytesPerMiB });
const METRIC_IDS = Object.freeze({ errors: 'errors' });
const DISPLAY = Object.freeze({ milliseconds: 'ms', percent: '%', operationsPerSecond: 'ops/s', kibibytes: 'KiB',
  mebibytes: 'MiB', higher: 'Higher is faster', lower: 'Lower is faster', better: 'Lower is better',
  generator: 'Lower generator usage', rss: 'Generator process usage' });

export const metrics = Object.freeze({
  throughput: Object.freeze({ title: 'Useful throughput', unit: DISPLAY.operationsPerSecond, direction: DISPLAY.higher, higher: true,
    read: measurement => measurement[VALUE_FIELDS.throughput] }),
  p50: Object.freeze({ title: 'Median request latency', unit: DISPLAY.milliseconds, direction: DISPLAY.lower,
    read: measurement => measurement[MEASUREMENT_FIELDS.latency][VALUE_FIELDS.p50] }),
  p95: Object.freeze({ title: 'Request latency · p95', unit: DISPLAY.milliseconds, direction: DISPLAY.lower,
    read: measurement => measurement[MEASUREMENT_FIELDS.latency][VALUE_FIELDS.p95] }),
  p99: Object.freeze({ title: 'Request latency · p99', unit: DISPLAY.milliseconds, direction: DISPLAY.lower,
    read: measurement => measurement[MEASUREMENT_FIELDS.latency][VALUE_FIELDS.p99] }),
  errors: Object.freeze({ title: 'Failed attempts', unit: DISPLAY.percent, direction: DISPLAY.better,
    read: measurement => measurement[MEASUREMENT_FIELDS.failures] / measurement[MEASUREMENT_FIELDS.attempts] * FACTORS.percent }),
  enqueue: Object.freeze({ title: 'Queue enqueue latency · p99', unit: DISPLAY.milliseconds, direction: DISPLAY.lower, queue: true,
    read: measurement => measurement[MEASUREMENT_FIELDS.enqueue]?.[VALUE_FIELDS.p99] }),
  receive: Object.freeze({ title: 'Queue receive latency · p99', unit: DISPLAY.milliseconds, direction: DISPLAY.lower, queue: true,
    read: measurement => measurement[MEASUREMENT_FIELDS.receive]?.[VALUE_FIELDS.p99] }),
  ack: Object.freeze({ title: 'Queue ACK latency · p99', unit: DISPLAY.milliseconds, direction: DISPLAY.lower, queue: true,
    read: measurement => measurement[MEASUREMENT_FIELDS.ack]?.[VALUE_FIELDS.p99] }),
  cpu: Object.freeze({ title: 'Load generator CPU per attempt', unit: DISPLAY.milliseconds, direction: DISPLAY.generator, client: true,
    read: measurement => measurement[MEASUREMENT_FIELDS.clientResources]
      ? measurement[MEASUREMENT_FIELDS.clientResources][MEASUREMENT_FIELDS.cpuSeconds] * FACTORS.milliseconds /
        measurement[MEASUREMENT_FIELDS.attempts] : null }),
  alloc: Object.freeze({ title: 'Load generator allocation per attempt', unit: DISPLAY.kibibytes, direction: DISPLAY.generator, client: true,
    read: measurement => measurement[MEASUREMENT_FIELDS.clientResources]
      ? measurement[MEASUREMENT_FIELDS.clientResources][MEASUREMENT_FIELDS.allocatedBytes] / FACTORS.kibibytes /
        measurement[MEASUREMENT_FIELDS.attempts] : null }),
  rss: Object.freeze({ title: 'Load generator observed peak RSS', unit: DISPLAY.mebibytes, direction: DISPLAY.rss, client: true,
    read: measurement => measurement[MEASUREMENT_FIELDS.clientResources]
      ? measurement[MEASUREMENT_FIELDS.clientResources][MEASUREMENT_FIELDS.peakRss] / FACTORS.mebibytes : null }),
});

const ROW_STATUS = Object.freeze({ failed: 'failed', measured: 'measured', unsupported: 'unsupported' });
const REPETITION = Object.freeze({ median: CONFIG.median });
const ORDER = Object.freeze({ zero: 0, one: 1, two: 2 });

export function median(values) {
  const sorted = values.filter(Number.isFinite).sort((left, right) => left - right);
  if (!sorted.length) return null;
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - ORDER.one] + sorted[middle]) / ORDER.two;
}

export function selectedRows(report, scenario, repetition, metric) {
  const definition = metrics[metric];
  if (!definition) throw new TypeError(TEXT.invalidReport);
  return report[REPORT_FIELDS.targets].map(target => {
    const cases = report[REPORT_FIELDS.cases].filter(item => item[CASE_FIELDS.target] === target[TARGET_FIELDS.name] &&
      item[CASE_FIELDS.scenario] === scenario &&
      (repetition === REPETITION.median || item[CASE_FIELDS.repetition] === Number(repetition)));
    const measurements = cases.map(item => item[CASE_FIELDS.measurement]).filter(Boolean);
    const values = measurements.map(definition.read).filter(Number.isFinite);
    const attempts = measurements.reduce((total, measurement) => total + measurement[MEASUREMENT_FIELDS.attempts], ORDER.zero);
    const failures = measurements.reduce((total, measurement) => total + measurement[MEASUREMENT_FIELDS.failures], ORDER.zero);
    const status = cases.some(item => item[CASE_FIELDS.status] === ROW_STATUS.failed) ? ROW_STATUS.failed
      : measurements.length ? ROW_STATUS.measured : ROW_STATUS.unsupported;
    const value = metric === METRIC_IDS.errors && attempts ? failures / attempts * FACTORS.percent : median(values);
    return {
      name: target[TARGET_FIELDS.name], status, value, min: values.length ? Math.min(...values) : null,
      max: values.length ? Math.max(...values) : null, attempts,
      successes: measurements.reduce((total, measurement) => total + measurement[MEASUREMENT_FIELDS.successes], ORDER.zero), failures,
      throughput: median(measurements.map(measurement => measurement[VALUE_FIELDS.throughput])),
      p50: median(measurements.map(measurement => measurement[MEASUREMENT_FIELDS.latency][VALUE_FIELDS.p50])),
      p95: median(measurements.map(measurement => measurement[MEASUREMENT_FIELDS.latency][VALUE_FIELDS.p95])),
      p99: median(measurements.map(measurement => measurement[MEASUREMENT_FIELDS.latency][VALUE_FIELDS.p99])),
      detail: cases.find(item => item[CASE_FIELDS.detail])?.[CASE_FIELDS.detail],
    };
  }).sort((left, right) => {
    if (left.value === null) return right.value === null ? ORDER.zero : ORDER.one;
    if (right.value === null) return -ORDER.one;
    return definition.higher ? right.value - left.value : left.value - right.value;
  });
}
