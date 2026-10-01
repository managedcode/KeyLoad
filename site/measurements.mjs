export const scenarios = {
  PointRead: ["Point read", "Primary-key reads returning the complete document."],
  DocumentWrite: ["Document write", "Unique document creates, with readback validation outside the timer."],
  VectorExact: ["Exact vector", "Exhaustive cosine top-K with the complete document projection, checked against an independent oracle."],
  QueueCycle: ["Queue cycle", "Enqueue → receive → ACK. Useful throughput counts unique, verified completed messages."],
  GraphNeighbors: ["Graph neighbors", "One-hop outgoing neighbors, returned as sorted distinct vertex IDs."],
  GraphTraverse: ["Graph traversal", "Bounded directed reachability with cycles and disconnected components, checked against breadth-first traversal."]
};
export const colors = { KeyLoad: "#243a2b", "PostgreSQL + pgvector": "#5c83b4", Qdrant: "#b67eaa", RabbitMQ: "#de9b51", Redis: "#cd7571", Neo4j: "#72a57a" };
export const metrics = {
  throughput: { title: "Useful throughput", unit: "ops/s", direction: "Higher is faster", higher: true, read: m => m.usefulOperationsPerSecond },
  p50: { title: "Median request latency", unit: "ms", direction: "Lower is faster", read: m => m.latency.p50Ms },
  p95: { title: "Request latency · p95", unit: "ms", direction: "Lower is faster", read: m => m.latency.p95Ms },
  p99: { title: "Request latency · p99", unit: "ms", direction: "Lower is faster", read: m => m.latency.p99Ms },
  errors: { title: "Failed attempts", unit: "%", direction: "Lower is better", read: m => m.failures / m.attempts * 100 },
  enqueue: { title: "Queue enqueue latency · p99", unit: "ms", direction: "Lower is faster", queue: true, read: m => m.enqueue?.p99Ms },
  receive: { title: "Queue receive latency · p99", unit: "ms", direction: "Lower is faster", queue: true, read: m => m.receive?.p99Ms },
  ack: { title: "Queue ACK latency · p99", unit: "ms", direction: "Lower is faster", queue: true, read: m => m.ack?.p99Ms },
  cpu: { title: "Load generator CPU per attempt", unit: "ms", direction: "Lower generator usage", client: true, read: m => m.clientResources ? m.clientResources.cpuSeconds * 1000 / m.attempts : null },
  alloc: { title: "Load generator allocation per attempt", unit: "KiB", direction: "Lower generator usage", client: true, read: m => m.clientResources ? m.clientResources.allocatedBytes / 1024 / m.attempts : null },
  rss: { title: "Load generator observed peak RSS", unit: "MiB", direction: "Generator process usage", client: true, read: m => m.clientResources ? m.clientResources.peakObservedWorkingSetBytes / 1048576 : null }
};
export function median(values) {
  const sorted = values.filter(value => Number.isFinite(value)).sort((a, b) => a - b);
  if (!sorted.length) return null;
  const i = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[i] : (sorted[i - 1] + sorted[i]) / 2;
}
export function selectedRows(report, scenario, repetition, metric) {
  return report.targets.map(target => {
    const cases = report.cases.filter(item => item.target === target.name && item.scenario === scenario && (repetition === "median" || item.repetition === Number(repetition)));
    const measurements = cases.map(item => item.measurement).filter(Boolean);
    const values = measurements.map(metrics[metric].read).filter(value => Number.isFinite(value));
    const attempts = measurements.reduce((total, m) => total + m.attempts, 0);
    const failures = measurements.reduce((total, m) => total + m.failures, 0);
    const status = cases.some(item => item.status === "failed") ? "failed" : measurements.length ? "measured" : "unsupported";
    const value = metric === "errors" && attempts ? failures / attempts * 100 : median(values);
    return { name: target.name, status, value, min: values.length ? Math.min(...values) : null, max: values.length ? Math.max(...values) : null,
      attempts, successes: measurements.reduce((total, m) => total + m.successes, 0), failures,
      throughput: median(measurements.map(m => m.usefulOperationsPerSecond)), p50: median(measurements.map(m => m.latency.p50Ms)),
      p95: median(measurements.map(m => m.latency.p95Ms)), p99: median(measurements.map(m => m.latency.p99Ms)), detail: cases.find(item => item.detail)?.detail };
  }).sort((a, b) => {
    if (a.value === null) return b.value === null ? 0 : 1;
    if (b.value === null) return -1;
    return metrics[metric].higher ? b.value - a.value : a.value - b.value;
  });
}
