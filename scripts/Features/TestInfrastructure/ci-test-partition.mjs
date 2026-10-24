import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { classifyClasses, constantValue, scanProject } from './ci-test-inventory.mjs';

const SHARED_LANES = new Set(['shared-readers', 'shared-exclusive']);
const OWN_LANES = new Set(['own-clusters', 'functional-gate']);
const LANE_KINDS = new Set([...SHARED_LANES, ...OWN_LANES, 'isolated-clusters']);

export function loadManifest(root) {
  return JSON.parse(readFileSync(path.join(root, 'scripts/Features/TestInfrastructure/ci-test-shards.json'), 'utf8'));
}

export function findShard(manifest, shardId) {
  for (const [suiteId, suite] of Object.entries(manifest.suites)) {
    const shard = suite.shards.find(candidate => candidate.id === shardId);
    if (shard) return { suiteId, suite, shard };
  }
  throw new Error(`Unknown test shard: ${shardId}`);
}

// Tree-node paths are /assembly/namespace/class/method; pinning the assembly keeps tests of
// referenced test assemblies (KeyLoad.RecoveryTests inside KeyLoad.UnitTests) out of the shard.
export function namespaceFilter(suite, namespace) {
  return `/${suite.assembly}/${namespace}/*/*`;
}

export function assemblyFilter(suite) {
  return `/${suite.assembly}/*/*/*`;
}

// Class names are unique in the assembly (checked below), so the namespace segment stays a wildcard.
// A single name stays literal, which lets TUnit narrow discovery to that class or method.
function alternation(names) {
  return names.length === 1 ? names[0] : `(${names.join('|')})`;
}

export function laneFilter(suite, lane) {
  if (lane.methods) return `/${suite.assembly}/*/${lane.class}/${alternation(lane.methods)}`;
  return `/${suite.assembly}/*/${alternation(lane.classes)}/*`;
}

function excluded(suite, scan, testClass) {
  const categories = [...testClass.categories, ...testClass.methods.flatMap(method => method.categories)]
    .map(expression => constantValue(scan.types, expression));
  return categories.some(category => (suite.excludedCategories ?? []).includes(category));
}

function checkNamespaces(suiteId, suite, classes, errors) {
  const owners = new Map();
  for (const shard of suite.shards) {
    for (const namespace of shard.namespaces ?? []) {
      if (owners.has(namespace)) errors.push(`${suiteId}: namespace ${namespace} is listed by ${owners.get(namespace)} and ${shard.id}.`);
      owners.set(namespace, shard.id);
    }
  }
  const present = new Set(classes.map(testClass => testClass.namespace));
  for (const namespace of present) {
    if (!owners.has(namespace)) errors.push(`${suiteId}: namespace ${namespace} has test classes but no shard lists it.`);
  }
  for (const namespace of owners.keys()) {
    if (!present.has(namespace)) errors.push(`${suiteId}: shard ${owners.get(namespace)} lists namespace ${namespace}, which has no test classes.`);
  }
  return testClass => ({ shard: owners.get(testClass.namespace), lane: testClass.namespace });
}

function assignLanes(suiteId, suite, errors) {
  const classOwners = new Map();
  const methodOwners = new Map();
  for (const shard of suite.shards) {
    if (shard.lanes.length > suite.lanesPerRunner) errors.push(`${suiteId}: shard ${shard.id} has ${shard.lanes.length} lanes; at most ${suite.lanesPerRunner} cluster lanes fit one runner.`);
    const laneIds = new Set();
    for (const lane of shard.lanes) {
      if (laneIds.has(lane.id)) errors.push(`${suiteId}: shard ${shard.id} repeats lane ${lane.id}.`);
      laneIds.add(lane.id);
      if (!LANE_KINDS.has(lane.kind)) errors.push(`${suiteId}: lane ${shard.id}/${lane.id} has unknown kind ${lane.kind}.`);
      const place = `${shard.id}/${lane.id}`;
      if (lane.methods) {
        for (const method of lane.methods) {
          const key = `${lane.class}.${method}`;
          if (methodOwners.has(key)) errors.push(`${suiteId}: method ${key} is listed by ${methodOwners.get(key).place} and ${place}.`);
          methodOwners.set(key, { shard: shard.id, lane: lane.id, place, kind: lane.kind, lanePlan: lane });
        }
        continue;
      }
      for (const name of lane.classes) {
        if (classOwners.has(name.toLowerCase())) errors.push(`${suiteId}: class ${name} is listed by ${classOwners.get(name.toLowerCase()).place} and ${place}.`);
        classOwners.set(name.toLowerCase(), { shard: shard.id, lane: lane.id, place, kind: lane.kind, lanePlan: lane });
      }
    }
  }
  return { classOwners, methodOwners };
}

function checkLaneCapacity(suiteId, suite, classified, ownerOf, errors) {
  const lanes = new Map();
  for (const testClass of classified) {
    for (const owner of ownerOf(testClass)) {
      const entry = lanes.get(owner.place) ?? { owner, classes: [] };
      entry.classes.push(testClass);
      lanes.set(owner.place, entry);
    }
  }
  for (const [place, { owner, classes }] of lanes) {
    const keys = new Set(classes.filter(item => item.cluster.startsWith('shared:')).map(item => item.cluster));
    const creators = classes.filter(item => item.createsOwnCluster).map(item => item.name);
    const sharedUsers = classes.filter(item => item.cluster.startsWith('shared:')).map(item => item.name);
    if (SHARED_LANES.has(owner.kind) && (keys.size > 1 || creators.length > 0)) {
      errors.push(`${suiteId}: shared lane ${place} must use one keyed cluster and no own clusters (${[...keys, ...creators].join(', ')}).`);
    }
    if (OWN_LANES.has(owner.kind) && sharedUsers.length > 0) {
      errors.push(`${suiteId}: own-cluster lane ${place} would keep a shared cluster alive beside its own clusters (${sharedUsers.join(', ')}).`);
    }
    if (owner.kind === 'isolated-clusters' && creators.length > 0) {
      errors.push(`${suiteId}: isolated lane ${place} must not create own clusters (${creators.join(', ')}).`);
    }
    const parallelism = owner.lanePlan.maximumParallelTests ?? suite.parallelism;
    const unsafe = classes.filter(item => item.disrupts.length > 0 || item.createsOwnCluster).map(item => item.name);
    if (parallelism > 1 && unsafe.length > 0) {
      errors.push(`${suiteId}: lane ${place} runs ${parallelism} tests at once but contains disruptive or own-cluster classes (${unsafe.join(', ')}).`);
    }
  }
}

function checkClasses(suiteId, suite, scan, classes, errors) {
  const names = new Map();
  for (const testClass of classes) {
    const key = testClass.name.toLowerCase();
    if (names.has(key)) errors.push(`${suiteId}: class name ${testClass.name} is declared in ${names.get(key)} and ${testClass.file}; class filters need unique names.`);
    names.set(key, testClass.file);
  }
  const { classOwners, methodOwners } = assignLanes(suiteId, suite, errors);
  const inventory = new Map(classes.map(testClass => [testClass.name.toLowerCase(), testClass]));
  for (const [key, owner] of classOwners) {
    if (!inventory.has(key)) errors.push(`${suiteId}: lane ${owner.place} lists ${owner.lanePlan.classes.find(name => name.toLowerCase() === key)}, which is not a test class.`);
  }
  for (const [key, owner] of methodOwners) {
    const [className, method] = key.split('.');
    const testClass = inventory.get(className.toLowerCase());
    if (!testClass?.methods.some(candidate => candidate.name === method)) errors.push(`${suiteId}: lane ${owner.place} lists ${key}, which is not a test method.`);
  }
  const ownerOf = testClass => {
    const classOwner = classOwners.get(testClass.name.toLowerCase());
    if (classOwner) return [classOwner];
    return testClass.methods.map(method => methodOwners.get(`${testClass.name}.${method.name}`)).filter(Boolean);
  };
  for (const testClass of classes) {
    const classOwner = classOwners.get(testClass.name.toLowerCase());
    const methodLanes = testClass.methods.filter(method => methodOwners.has(`${testClass.name}.${method.name}`));
    if (excluded(suite, scan, testClass)) {
      if (classOwner || methodLanes.length > 0) errors.push(`${suiteId}: ${testClass.name} has an excluded category but is assigned to a lane.`);
      continue;
    }
    if (classOwner && methodLanes.length > 0) errors.push(`${suiteId}: ${testClass.name} is listed as a class and by method lanes.`);
    if (!classOwner && methodLanes.length === 0) errors.push(`${suiteId}: test class ${testClass.name} (${testClass.file}) is in no lane.`);
    if (!classOwner && methodLanes.length > 0 && methodLanes.length < testClass.methods.length) {
      const unassigned = testClass.methods.filter(method => !methodOwners.has(`${testClass.name}.${method.name}`));
      errors.push(`${suiteId}: ${testClass.name} methods ${unassigned.map(method => method.name).join(', ')} are in no lane.`);
    }
  }
  const active = classes.filter(testClass => !excluded(suite, scan, testClass));
  checkLaneCapacity(suiteId, suite, classifyClasses({ ...scan, classes: active }), ownerOf, errors);
  return (testClass, method) => classOwners.get(testClass.name.toLowerCase()) ?? methodOwners.get(`${testClass.name}.${method}`);
}

// Static exactly-once partition of every test project the workflow runs.
export function checkPartition(root, manifest) {
  const errors = [];
  const shardIds = new Set();
  const suites = new Map();
  for (const [suiteId, suite] of Object.entries(manifest.suites)) {
    for (const shard of suite.shards) {
      if (shardIds.has(shard.id)) errors.push(`Shard id ${shard.id} is used more than once.`);
      shardIds.add(shard.id);
    }
    const scan = scanProject(root, suite.sources);
    const classes = scan.classes;
    let ownerOf;
    if (suite.partition === 'namespace') ownerOf = checkNamespaces(suiteId, suite, classes, errors);
    else if (suite.partition === 'class') ownerOf = checkClasses(suiteId, suite, scan, classes, errors);
    else {
      if (suite.shards.length !== 1) errors.push(`${suiteId}: an assembly partition needs exactly one shard.`);
      ownerOf = () => ({ shard: suite.shards[0]?.id });
    }
    suites.set(suiteId, { suite, scan, classes: classes.filter(testClass => !excluded(suite, scan, testClass)), ownerOf });
  }
  return { errors, suites };
}

export function trxFiles(directory) {
  const found = [];
  const walk = current => {
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      const full = path.join(current, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.isFile() && entry.name.endsWith('.trx')) found.push(full);
    }
  };
  walk(directory);
  return found.sort();
}

function attributes(tag) {
  const values = {};
  for (const match of tag.matchAll(/([\w:]+)="([^"]*)"/g)) {
    values[match[1]] = match[2].replace(/&quot;/g, '"').replace(/&apos;/g, '\'').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
  }
  return values;
}

export function readTrx(file) {
  const text = readFileSync(file, 'utf8');
  const definitions = new Map();
  for (const match of text.matchAll(/<UnitTest\s([^>]*)>([\s\S]*?)<\/UnitTest>/g)) {
    const method = /<TestMethod\s([^>]*)\/?>/.exec(match[2]);
    if (!method) continue;
    const values = attributes(method[1]);
    definitions.set(attributes(match[1]).id, {
      host: path.basename(values.codeBase ?? '').replace(/\.dll$/i, ''),
      className: values.className,
      method: values.name,
    });
  }
  return [...text.matchAll(/<UnitTestResult\s([^>]*?)\/?>/g)].map(match => {
    const values = attributes(match[1]);
    return { ...definitions.get(values.testId), testId: values.testId, testName: values.testName, outcome: values.outcome, file };
  });
}
