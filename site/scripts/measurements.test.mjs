import test from "node:test";
import assert from "node:assert/strict";
import { median, selectedRows } from "../measurements.mjs";

test("all repetitions determine the median and range; failure rate retains every attempt", () => {
  const measurement = (throughput, failures = 0) => ({ attempts: 10, successes: 10 - failures, failures,
    usefulOperationsPerSecond: throughput, latency: { p50Ms: 1, p95Ms: 5, p99Ms: 10 } });
  const report = { targets: [{ name: "KeyLoad" }, { name: "RabbitMQ" }], cases: [
    { target: "KeyLoad", scenario: "PointRead", repetition: 0, status: "measured", measurement: measurement(100) },
    { target: "KeyLoad", scenario: "PointRead", repetition: 1, status: "failed", measurement: measurement(10, 1) },
    { target: "KeyLoad", scenario: "PointRead", repetition: 2, status: "measured", measurement: measurement(30) },
    ...[0, 1, 2].map(repetition => ({ target: "RabbitMQ", scenario: "PointRead", repetition, status: "unsupported", measurement: null }))
  ] };
  const [row, unsupported] = selectedRows(report, "PointRead", "median", "throughput");
  assert.equal(row.value, 30); assert.equal(row.min, 10); assert.equal(row.max, 100); assert.equal(row.status, "failed");
  assert.equal(row.successes, 29); assert.equal(row.attempts, 30); assert.equal(unsupported.value, null);
  assert.equal(selectedRows(report, "PointRead", "median", "errors")[0].value, 100 / 30);
  assert.equal(selectedRows(report, "PointRead", "0", "throughput")[0].value, 100);
  assert.equal(median([2, 4]), 3); assert.equal(median([]), null);
});
