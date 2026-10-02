import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { A, C, F, EvidenceError, fail } from './github-evidence-contracts.mjs';
import { selectEvidence } from './github-evidence-runs.mjs';
import { proveEvidence, verifyArchive, verifyFreshness } from './github-evidence-proof.mjs';

const commands = new Map([
  [C.cmdSelect, select], [C.cmdProve, prove], [C.cmdArchive, archive], [C.cmdFresh, fresh],
]);

export async function runCli(argv) {
  try {
    const [command, ...rawArguments] = argv;
    const execute = commands.get(command);
    if (!execute) fail(C.errorArgument, C.messages.unsupportedCommand);
    const argumentsMap = parseArguments(rawArguments);
    const result = await execute(argumentsMap);
    return { exitCode: 0, envelope: { [F.ok]: true, [F.result]: result } };
  } catch (error) {
    const code = error instanceof EvidenceError ? error.code : C.errorCapture;
    const message = error instanceof Error ? error.message : C.messages.evidenceValidationFailed;
    return { exitCode: 1, envelope: { [F.ok]: false, [F.error]: { [F.errorCode]: code, [F.message]: message } } };
  }
}

function parseArguments(raw) {
  const values = new Map();
  for (const argument of raw) {
    const separator = argument.indexOf(A.separator);
    if (!argument.startsWith(A.prefix) || separator < A.prefix.length + C.oneCharacter) fail(C.errorArgument, C.messages.invalidArguments);
    const key = argument.slice(A.prefix.length, separator);
    if (values.has(key)) fail(C.errorArgument, C.messages.invalidArguments);
    values.set(key, argument.slice(separator + 1));
  }
  return values;
}

async function select(values) {
  requireOnly(values, [A.input, A.mode, A.requestedRun]);
  const root = requiredAbsolute(values, A.input);
  const mode = requiredValue(values, A.mode);
  const result = await selectEvidence(root, mode, values.get(A.requestedRun) ?? null);
  const { trail, ...publicResult } = result;
  return publicResult;
}

async function prove(values) {
  requireOnly(values, [A.input, A.mode, A.requestedRun, A.siteRevision, A.workflowRevision]);
  const root = requiredAbsolute(values, A.input);
  return proveEvidence(root, requiredValue(values, A.mode), values.get(A.requestedRun) ?? null,
    requiredValue(values, A.siteRevision), requiredValue(values, A.workflowRevision));
}

async function archive(values) {
  requireOnly(values, [A.receipt, A.archive]);
  return verifyArchive(requiredAbsolute(values, A.receipt), requiredAbsolute(values, A.archive));
}

async function fresh(values) {
  requireOnly(values, [A.before, A.after]);
  return verifyFreshness(requiredAbsolute(values, A.before), requiredAbsolute(values, A.after));
}

function requireOnly(values, allowed) {
  if ([...values.keys()].some(key => !allowed.includes(key))) fail(C.errorArgument, C.messages.invalidArguments);
}

function requiredValue(values, key) {
  const value = values.get(key);
  if (!value) fail(C.errorArgument, `${C.messages.requiredArgumentPrefix}${key}${C.messages.requiredArgumentSuffix}`);
  return value;
}

function requiredAbsolute(values, key) {
  const value = requiredValue(values, key);
  if (!path.isAbsolute(value)) fail(C.errorArgument, `${C.messages.absoluteArgumentPrefix}${key}${C.messages.absoluteArgumentSuffix}`);
  return value;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  const { exitCode, envelope } = await runCli(process.argv.slice(2));
  process.stdout.write(`${JSON.stringify(envelope)}\n`);
  process.exitCode = exitCode;
}
