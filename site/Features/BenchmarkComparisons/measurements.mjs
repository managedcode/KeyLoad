import { CONFIG } from './contracts.mjs';

const VALUE_FIELDS = Object.freeze({ throughput: 'usefulOperationsPerSecond', p50: 'p50Ms', p95: 'p95Ms', p99: 'p99Ms' });
const MEASUREMENT_FIELDS = Object.freeze({ latency: 'latency', enqueue: 'enqueue', receive: 'receive', ack: 'ack',
  clientResources: 'clientResources', cpuSeconds: 'cpuSeconds', allocatedBytes: 'allocatedBytes', peakRss: 'peakObservedWorkingSetBytes',
  attempts: 'attempts', failures: 'failures', successes: 'successes' });
const FACTORS = Object.freeze({ percent: CONFIG.units.percent, milliseconds: CONFIG.units.millisecondsPerSecond,
  kibibytes: CONFIG.units.bytesPerKiB, mebibytes: CONFIG.units.bytesPerMiB });
const DISPLAY = Object.freeze({ milliseconds: 'ms', percent: '%', operationsPerSecond: 'ops/s', kibibytes: 'KiB',
  mebibytes: 'MiB', higher: 'Higher is faster', lower: 'Lower is faster', better: 'Lower is better',
  generator: 'Shared process observation', rss: 'Process observation over lane interval' });

export const metrics = Object.freeze({
  duration: Object.freeze({ title: 'Actual elapsed time', unit: 's', direction: DISPLAY.lower,
    read: measurement => measurement.elapsedSeconds }),
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
  cpu: Object.freeze({ title: 'Process CPU per lane attempt', unit: DISPLAY.milliseconds, direction: DISPLAY.generator, client: true,
    read: measurement => measurement[MEASUREMENT_FIELDS.clientResources]
      ? measurement[MEASUREMENT_FIELDS.clientResources][MEASUREMENT_FIELDS.cpuSeconds] * FACTORS.milliseconds /
        measurement[MEASUREMENT_FIELDS.attempts] : null }),
  alloc: Object.freeze({ title: 'Process allocation per lane attempt', unit: DISPLAY.kibibytes, direction: DISPLAY.generator, client: true,
    read: measurement => measurement[MEASUREMENT_FIELDS.clientResources]
      ? measurement[MEASUREMENT_FIELDS.clientResources][MEASUREMENT_FIELDS.allocatedBytes] / FACTORS.kibibytes /
        measurement[MEASUREMENT_FIELDS.attempts] : null }),
  rss: Object.freeze({ title: 'Process peak RSS · lane interval', unit: DISPLAY.mebibytes, direction: DISPLAY.rss, client: true,
    read: measurement => measurement[MEASUREMENT_FIELDS.clientResources]
      ? measurement[MEASUREMENT_FIELDS.clientResources][MEASUREMENT_FIELDS.peakRss] / FACTORS.mebibytes : null }),
});

