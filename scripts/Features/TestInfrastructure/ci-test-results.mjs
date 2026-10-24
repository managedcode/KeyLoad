import path from 'node:path';
import { readTrx, trxFiles } from './ci-test-partition.mjs';

// TRX reports are written to <results>/<suite>/<shard>/<lane or namespace>/<report>.trx.
function locate(resultsDirectory, file, shardIds) {
  const segments = path.relative(resultsDirectory, file).split(path.sep);
  const index = segments.findIndex(segment => shardIds.has(segment));
  if (index < 0) return {};
  return { shard: segments[index], lane: index + 1 < segments.length - 1 ? segments[index + 1] : undefined };
}

function indexInventory(partition) {
  const classes = new Map();
  for (const [suiteId, entry] of partition.suites) {
    for (const testClass of entry.classes) classes.set(`${suiteId}|${testClass.namespace}.${testClass.name}`, testClass);
  }
  return classes;
}

function recordIdentity(identities, result) {
  const identity = `${result.className}.${result.testName}`;
  const runs = identities.get(identity) ?? [];
  runs.push(result);
  identities.set(identity, runs);
}

function placementError(entry, testClass, result, location, relative) {
  if (!location.shard) return null;
  const owner = entry.ownerOf(testClass, result.method);
  if (!owner) return null;
  const wrongShard = owner.shard !== location.shard;
  const wrongLane = owner.lane !== undefined && location.lane !== undefined && owner.lane !== location.lane;
  if (!wrongShard && !wrongLane) return null;
  const subject = owner.lanePlan?.methods ? `${result.className}.${result.method}` : result.className;
  return `${subject} ran in ${location.shard}/${location.lane ?? '-'} (${relative}) but is assigned to ${owner.shard}/${owner.lane ?? '-'}.`;
}

// Executed TRX identities: no test runs twice, every inventory class (and split method) runs, nothing unknown runs.
export function checkResults(resultsDirectory, manifest, partition) {
  const errors = [];
  const files = trxFiles(resultsDirectory);
  if (files.length === 0) return { errors: [`No TRX reports were found under ${resultsDirectory}.`], counts: new Map() };
  const hostSuites = new Map(Object.entries(manifest.suites).map(([suiteId, suite]) => [suite.assembly, suiteId]));
  const shardIds = new Set(Object.values(manifest.suites).flatMap(suite => suite.shards.map(shard => shard.id)));
  const inventory = indexInventory(partition);
  const identities = new Map();
  const executed = new Map();
  const counts = new Map();
  const reported = new Set();
  for (const file of files) {
    const relative = path.relative(resultsDirectory, file);
    const location = locate(resultsDirectory, file, shardIds);
    for (const result of readTrx(file)) {
      if (!result.className) {
        errors.push(`${relative}: result ${result.testId} has no test definition.`);
        continue;
      }
      const suiteId = hostSuites.get(result.host);
      if (!suiteId) {
        if (!reported.has(result.host)) errors.push(`${relative}: test host ${result.host} belongs to no suite.`);
        reported.add(result.host);
        continue;
      }
      counts.set(suiteId, (counts.get(suiteId) ?? 0) + 1);
      recordIdentity(identities, result);
      const key = `${suiteId}|${result.className}`;
      const methods = executed.get(key) ?? new Set();
      methods.add(result.method);
      executed.set(key, methods);
      const testClass = inventory.get(key);
      if (!testClass) {
        if (!reported.has(key)) errors.push(`${result.className} ran in ${suiteId} (${relative}) but is not a ${suiteId} test class in the source inventory.`);
        reported.add(key);
        continue;
      }
      const placement = placementError(partition.suites.get(suiteId), testClass, result, location, relative);
      if (placement && !reported.has(placement)) errors.push(placement);
      if (placement) reported.add(placement);
    }
  }
  for (const [identity, runs] of identities) {
    const sources = new Set(runs.map(run => run.file));
    const ids = new Set(runs.map(run => run.testId));
    if (sources.size > 1 || ids.size < runs.length) {
      errors.push(`${identity} executed ${runs.length} times: ${[...sources].map(file => path.relative(resultsDirectory, file)).join(', ')}.`);
    }
  }
  for (const [suiteId, entry] of partition.suites) {
    if (!counts.has(suiteId)) {
      errors.push(`Suite ${suiteId} has no TRX results.`);
      continue;
    }
    for (const testClass of entry.classes) {
      const methods = executed.get(`${suiteId}|${testClass.namespace}.${testClass.name}`);
      if (!methods) {
        errors.push(`${suiteId}: ${testClass.namespace}.${testClass.name} (${testClass.file}) never executed.`);
        continue;
      }
      for (const method of testClass.methods) {
        if (entry.ownerOf(testClass, method.name)?.lanePlan?.methods && !methods.has(method.name)) {
          errors.push(`${suiteId}: ${testClass.name}.${method.name} never executed.`);
        }
      }
    }
  }
  return { errors, counts, files: files.length };
}
