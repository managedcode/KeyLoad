import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';

// Source inventory of TUnit test classes. CI reads it without building, so it scans C# text:
// file-scoped namespaces, column-0 type declarations and [Test] methods with their [Arguments] cases.
const SKIPPED_DIRECTORIES = new Set(['bin', 'obj']);
const DECLARATION = /^((?:\[[^\n]*\]\s*)*)(?:(?:public|internal|private|protected|file|sealed|static|abstract|partial|readonly|unsafe|ref)\s+)*(?:record\s+(?:struct|class)|class|record|struct|interface|enum)\s+([A-Z]\w*)/gm;
const METHOD = /^(?:(?:public|internal|private|protected|static|async|virtual|override|sealed)\s+)+(?:Task|ValueTask|void)(?:<[^>]*>)?\s+(\w+)\s*\(/;
const TEST_ATTRIBUTE = /\bTest(?:Attribute)?\b/;
const ARGUMENTS = /\bArguments(?:Attribute)?\(/g;
const CONSTANT = /\bconst\s+string\s+(\w+)\s*=\s*"([^"]*)"/g;
const CLASS_DATA_SOURCE = /ClassDataSource<(\w+)>\(([^)]*)\)/g;
const CATEGORY = /\bCategory\(([^)]*)\)/g;
const DISRUPTIVE_CALLS = {
  kill: /\.KillServerAsync\(|KillAndJoinAsync\(|\.KillAsync\(/,
  restart: /\.RestartServerAsync\(|\.BeginProcessRestartAsync\(/,
  'resource-command': /ExecuteCommandAsync\(|KnownResourceCommands\.|RequireProcessRuntime\(\)/,
  'silo-stop': /StopSiloAsync\(/,
};
const OWN_CLUSTER = /\bFunctionalGate\w*Fixture\b|\bnew\s+ClusterFixture\b|ClusterFixture\s+\w+\s*=\s*new\s*\(/;
const HOSTED_CLUSTER = /RequestCqrsHostedCluster\./;
const CHILD_PROCESS = /ProcessStartInfo|Process\.Start\(/;
const TRAVERSAL_STOPS = new Set(['ClusterFixture', 'ProcessRuntimeControl']);

export function sourceFiles(directory) {
  const files = [];
  const walk = current => {
    for (const entry of readdirSync(current, { withFileTypes: true })) {
      if (entry.isSymbolicLink()) continue;
      const full = path.join(current, entry.name);
      if (entry.isDirectory()) {
        if (!SKIPPED_DIRECTORIES.has(entry.name)) walk(full);
      } else if (entry.isFile() && entry.name.endsWith('.cs')) files.push(full);
    }
  };
  walk(directory);
  return files.sort();
}

// Removes comments while keeping single-line literals, so URLs, keys and filter strings stay intact.
// Multi-line literals (embedded C# in raw strings) are blanked so their declarations are not scanned.
export function stripComments(text) {
  let output = '';
  let index = 0;
  while (index < text.length) {
    const current = text[index];
    const next = text[index + 1];
    if (current === '/' && next === '/') {
      while (index < text.length && text[index] !== '\n') index++;
    } else if (current === '/' && next === '*') {
      const end = text.indexOf('*/', index + 2);
      const stop = end < 0 ? text.length : end + 2;
      output += text.slice(index, stop).replace(/[^\n]/g, '');
      index = stop;
    } else if (current === '"' || current === '\'') {
      const stop = literalEnd(text, index);
      const literal = text.slice(index, stop);
      output += literal.includes('\n') ? literal.replace(/[^\n]/g, ' ') : literal;
      index = stop;
    } else {
      output += current;
      index++;
    }
  }
  return output;
}

function literalEnd(text, start) {
  const quote = text[start];
  if (quote === '"' && text.startsWith('"""', start)) {
    let count = 3;
    while (text[start + count] === '"') count++;
    const end = text.indexOf('"'.repeat(count), start + count);
    return end < 0 ? text.length : end + count;
  }
  const verbatim = quote === '"' && (text[start - 1] === '@' || (text[start - 1] === '$' && text[start - 2] === '@'));
  let index = start + 1;
  while (index < text.length) {
    if (verbatim && text[index] === '"' && text[index + 1] === '"') { index += 2; continue; }
    if (!verbatim && text[index] === '\\') { index += 2; continue; }
    if (text[index] === quote) return index + 1;
    if (!verbatim && text[index] === '\n') return index;
    index++;
  }
  return text.length;
}

function attributeHead(lines, declarationLine) {
  const collected = [];
  for (let index = declarationLine - 1; index >= 0; index--) {
    const line = lines[index].trim();
    if (line === '' || line.startsWith('namespace ') || line.startsWith('using ') || line.endsWith(';') || line.endsWith('}')) break;
    collected.unshift(line);
  }
  return collected.join(' ');
}

function testMethods(body) {
  const methods = [];
  const lines = body.split('\n');
  let attributes = '';
  for (let index = 0; index < lines.length; index++) {
    const line = lines[index].trim();
    if (line.startsWith('[')) {
      let block = line;
      while ((block.match(/\[/g) ?? []).length > (block.match(/\]/g) ?? []).length && index + 1 < lines.length) {
        block += ` ${lines[++index].trim()}`;
      }
      attributes += ` ${block}`;
      continue;
    }
    const method = METHOD.exec(line);
    if (method && TEST_ATTRIBUTE.test(attributes)) {
      methods.push({
        name: method[1],
        cases: Math.max(1, (attributes.match(ARGUMENTS) ?? []).length),
        categories: [...attributes.matchAll(CATEGORY)].map(match => match[1].trim()),
      });
    }
    if (line !== '') attributes = '';
  }
  return methods;
}

export function parseSource(text) {
  const clean = stripComments(text);
  const namespace = /^namespace\s+([\w.]+)\s*;/m.exec(clean)?.[1] ?? '';
  const lines = clean.split('\n');
  const matches = [...clean.matchAll(DECLARATION)];
  const types = matches.map((match, position) => {
    const declarationLine = clean.slice(0, match.index).split('\n').length - 1;
    const end = position + 1 < matches.length ? matches[position + 1].index : clean.length;
    const body = clean.slice(match.index, end);
    const head = `${attributeHead(lines, declarationLine)} ${match[0]}`;
    const constants = [...body.matchAll(CONSTANT)].map(constant => [constant[1], constant[2]]);
    return { name: match[2], namespace, head, body, constants };
  });
  return { namespace, types };
}

export function scanProject(root, sources) {
  const types = new Map();
  for (const file of sourceFiles(path.resolve(root, sources))) {
    const relative = path.relative(root, file).split(path.sep).join('/');
    for (const type of parseSource(readFileSync(file, 'utf8')).types) {
      const entry = types.get(type.name) ?? { name: type.name, namespace: type.namespace, parts: [] };
      entry.parts.push({ ...type, file: relative });
      types.set(type.name, entry);
    }
  }
  const classes = [];
  for (const entry of types.values()) {
    for (const part of entry.parts) {
      const methods = testMethods(part.body);
      if (methods.length === 0) continue;
      classes.push({
        name: entry.name,
        namespace: part.namespace,
        file: part.file,
        head: part.head,
        methods,
        cases: methods.reduce((sum, method) => sum + method.cases, 0),
        categories: [...part.head.matchAll(CATEGORY)].map(match => match[1].trim()),
      });
    }
  }
  return { types, classes: classes.sort((left, right) => left.name.localeCompare(right.name)) };
}

// Resolves a category argument such as HeavyDocumentLoadProtocol.Category or "HeavyLoad" to its text.
export function constantValue(types, expression) {
  const literal = /^"([^"]*)"$/.exec(expression.trim());
  if (literal) return literal[1];
  const [typeName, constantName] = expression.trim().split('.');
  const entry = types.get(typeName);
  const match = entry?.parts.flatMap(part => part.constants).find(([name]) => name === constantName);
  return match ? match[1] : expression.trim();
}

function references(types, text, extensionOwners) {
  const found = new Set();
  for (const match of text.matchAll(/(?<![.\w])([A-Z]\w+)(?=\s*\.\s*[A-Z]|\s*<[^>]*>\s*\.)/g)) found.add(match[1]);
  for (const match of text.matchAll(/\bnew\s+([A-Z]\w+)\b/g)) found.add(match[1]);
  for (const match of text.matchAll(/\.(\w+)\s*\(/g)) for (const owner of extensionOwners.get(match[1]) ?? []) found.add(owner);
  return [...found].filter(name => types.has(name));
}

// Class-level reachability through helper types: which clusters a test class uses and whether it disrupts them.
export function classifyClasses(scan) {
  const extensionOwners = new Map();
  for (const entry of scan.types.values()) {
    for (const part of entry.parts) {
      for (const match of part.body.matchAll(/static\s+[\w<>[\],.? ]+?\s+(\w+)\s*(?:<[^>]*>)?\s*\(\s*this\s/g)) {
        extensionOwners.set(match[1], [...(extensionOwners.get(match[1]) ?? []), entry.name]);
      }
    }
  }
  const facts = new Map();
  for (const entry of scan.types.values()) {
    const text = entry.parts.map(part => part.body).join('\n');
    facts.set(entry.name, {
      calls: Object.entries(DISRUPTIVE_CALLS).filter(([, pattern]) => pattern.test(text)).map(([kind]) => kind),
      own: OWN_CLUSTER.test(text),
      hosted: HOSTED_CLUSTER.test(text),
      process: CHILD_PROCESS.test(text),
      references: references(scan.types, text, extensionOwners).filter(name => name !== entry.name),
    });
  }
  return scan.classes.map(testClass => {
    const reached = new Set([testClass.name]);
    const pending = [testClass.name];
    while (pending.length > 0) {
      for (const next of facts.get(pending.pop()).references) {
        if (TRAVERSAL_STOPS.has(next) || reached.has(next)) continue;
        reached.add(next);
        pending.push(next);
      }
    }
    const reachedFacts = [...reached].map(name => facts.get(name));
    const keys = [...testClass.head.matchAll(CLASS_DATA_SOURCE)]
      .filter(match => match[1] === 'ClusterFixture')
      .map(match => /Key\s*=\s*([^,)]+)/.exec(match[2])?.[1])
      .filter(Boolean)
      .map(expression => constantValue(scan.types, expression));
    const own = reachedFacts.some(fact => fact.own);
    const hosted = reachedFacts.some(fact => fact.hosted);
    const cluster = keys.length > 0 ? `shared:${keys[0]}` : own ? 'own' : hosted ? 'own-hosted'
      : reachedFacts.some(fact => fact.process) ? 'process' : 'none';
    const disrupts = [...new Set(reachedFacts.flatMap(fact => fact.calls))].sort();
    return { ...testClass, cluster, disrupts, createsOwnCluster: own || hosted };
  });
}
