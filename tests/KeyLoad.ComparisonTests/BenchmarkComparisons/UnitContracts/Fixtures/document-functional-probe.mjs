// Controlled native evidence exercises the production validator, never authenticates GitHub or qualifies a workload.
import path from 'node:path';
import os from 'node:os';
import { mkdtemp, mkdir, writeFile, appendFile, rm, realpath } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { FUNCTIONAL, validateDocumentFunctionalProof, selectDocumentFunctionalGate, parseDocumentFunctionalTrx,
  verifyDocumentFunctionalGate } from '../../../../../scripts/Features/BenchmarkComparisons/document-functional-gate.mjs';
import { runBounded } from '../../../../../scripts/Features/BenchmarkComparisons/native-process.mjs';
import { hashRegularFile } from '../../../../../scripts/Features/BenchmarkComparisons/isolated-github-files.mjs';

const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const originalsRoot = 'TestResults/cluster-heavy-load/qualification/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/';
const entries = ['TestResults/cluster-heavy-load/original.trx', ...FUNCTIONAL.files.slice(1).map(file => originalsRoot + path.basename(file))];
const binary = { executablePath: '/usr/share/dotnet/dotnet', executableSha256: '1'.repeat(64),
  assemblyPath: '/workspace/src/KeyLoad.Server/bin/Debug/net10.0/KeyLoad.Server.dll', assemblySha256: '2'.repeat(64),
  pdbPath: '/workspace/src/KeyLoad.Server/bin/Debug/net10.0/KeyLoad.Server.pdb', pdbSha256: '3'.repeat(64),
  sourceCount: 12, sourceCompositionSha256: '4'.repeat(64) };
const processes = Array.from({length: 3}, (_, index) => ({ name: 'node' + (index + 1), processId: 100 + index,
  startedAtUtc: '2026-10-09T10:01:00Z', aspireStartedAtUtc: '2026-10-09T10:00:59Z',
  aspireProcessId: 200 + index, aspireProcessStartedAtUtc: '2026-10-09T10:00:59Z', binary }));
const runtimeIdentity = { descriptor: { workspace: '/workspace', assemblies: [{role: 'server',
  path: 'src/KeyLoad.Server/bin/Debug/net10.0/KeyLoad.Server.dll', sha256: binary.assemblySha256,
  pdbPath: 'src/KeyLoad.Server/bin/Debug/net10.0/KeyLoad.Server.pdb', pdbSha256: binary.pdbSha256}] } };
const pascal = value => Object.fromEntries(Object.entries(value).map(([key, item]) =>
  [key[0].toUpperCase() + key.slice(1), key === 'binary' ? pascal(item) : item]));
export function controlledFunctionalProof(cohort) {
  return { job: { id: 9000, name: FUNCTIONAL.job,
    url: `https://github.com/${cohort.repository}/actions/runs/${cohort.runId}/job/9000`, conclusion: 'success',
    steps: FUNCTIONAL.steps.map(name => ({ name, conclusion: 'success' })) },
    artifact: { id: 9001, name: FUNCTIONAL.artifact, sizeInBytes: 1, digest: 'sha256:' + 'a'.repeat(64), expired: false },
    archive: { path: FUNCTIONAL.archive, bytes: 1, sha256: 'a'.repeat(64) },
    files: FUNCTIONAL.files.map((file, index) => ({ path: file, entry: entries[index], bytes: 1, sha256: 'b'.repeat(64) })),
    test: { className: FUNCTIONAL.className, methodName: FUNCTIONAL.methodName, instanceName: FUNCTIONAL.methodName, count: 1 },
    processes: structuredClone(processes) };
}
function originalTrx(outcome = 'Passed', extra = false) {
  const definition = id => `<UnitTest id="${id}" name="${FUNCTIONAL.methodName}"><TestMethod className="${FUNCTIONAL.className}" name="${FUNCTIONAL.methodName}" /></UnitTest>`;
  const result = id => `<UnitTestResult testId="${id}" testName="${FUNCTIONAL.methodName}" outcome="${outcome}" />`;
  return `<TestRun><TestDefinitions>${definition('1')}${extra ? definition('2') : ''}</TestDefinitions><Results>${result('1')}${extra ? result('2') : ''}</Results><ResultSummary><Counters total="${extra ? 2 : 1}" executed="${extra ? 2 : 1}" passed="${extra ? 2 : 1}" failed="0" notExecuted="0" /></ResultSummary></TestRun>`;
}
async function rejected(action) { try { await action(); return false; } catch { return true; } }
const cohort = { sourceRevision: 'a'.repeat(40), runId: 123, attempt: 1, repository: 'managedcode/KeyLoad',
  ref: 'refs/heads/main', workflow: 'Benchmarks', profile: 'controlled' };
function captureOf(proof) {
  const started = '2026-10-09T10:00:00Z'; const completed = '2026-10-09T11:00:00Z';
  return { run: { id: cohort.runId, head_sha: cohort.sourceRevision, head_branch: 'main', repository: { id: 1 }, head_repository: { id: 1 } },
    jobs: [{ id: proof.job.id, name: proof.job.name, run_id: cohort.runId, run_attempt: cohort.attempt, head_sha: cohort.sourceRevision,
      html_url: proof.job.url, status: 'completed', conclusion: 'success', started_at: started, completed_at: completed,
      steps: FUNCTIONAL.steps.map((name, index) => ({ name, number: index + 1, status: 'completed', conclusion: 'success' })) }],
    artifacts: [{ id: proof.artifact.id, name: proof.artifact.name, size_in_bytes: proof.artifact.sizeInBytes,
      digest: proof.artifact.digest, expired: false, created_at: '2026-10-09T10:59:00Z',
      workflow_run: { id: cohort.runId, head_sha: cohort.sourceRevision, head_branch: 'main', repository_id: 1, head_repository_id: 1 } }] };
}
async function probe() {
  const directory = await mkdtemp(path.join(await realpath(os.tmpdir()), 'keyload-functional-gate-'));
  try {
    const trx = path.join(directory, 'original.trx');
    await writeFile(trx, originalTrx());
    const positive = await parseDocumentFunctionalTrx(trx, directory);
    const xmlMutations = [originalTrx('NotExecuted'), originalTrx('Failed'), originalTrx('Passed', true),
      originalTrx().replace('passed="1"', 'passed="0"'), originalTrx().replace(FUNCTIONAL.className, 'Foreign.Test'),
      '<!DOCTYPE TestRun [<!ENTITY x SYSTEM "file:///etc/passwd">]>' + originalTrx()];
    const xmlRejected = [];
    for (const xml of xmlMutations) { await writeFile(trx, xml); xmlRejected.push(await rejected(() => parseDocumentFunctionalTrx(trx, directory))); }
    const proof = controlledFunctionalProof(cohort);
    validateDocumentFunctionalProof(proof, cohort);
    const capture = captureOf(proof);
    selectDocumentFunctionalGate(capture, cohort);
    const captureMutations = [value => value.jobs[0].conclusion = 'failure', value => value.jobs[0].steps[0].conclusion = 'skipped',
      value => value.jobs[0].run_attempt = 2, value => value.jobs[0].head_sha = 'f'.repeat(40),
      value => value.jobs.push(structuredClone(value.jobs[0])), value => value.artifacts[0].expired = true,
      value => value.artifacts[0].workflow_run.head_sha = 'f'.repeat(40), value => value.artifacts.pop()];
    const captureRejected = [];
    for (const change of captureMutations) { const value = structuredClone(capture); change(value); captureRejected.push(await rejected(() => selectDocumentFunctionalGate(value, cohort))); }
    const input = path.join(directory, 'intake'); const source = path.join(directory, 'source');
    await mkdir(path.join(input, 'archives'), { recursive: true });
    await mkdir(path.join(input, 'github/functional'), { recursive: true });
    const started = processes.map(pascal);
    const closed = { JoinedAtUtc: '2026-10-09T10:59:01Z', Processes: started, Resources: started.map(value => ({
      Name: value.Name, ProcessId: value.ProcessId, StartedAtUtc: value.StartedAtUtc,
      AspireStartedAtUtc: value.AspireStartedAtUtc, ExitCode: 0, StartTimeStamp: value.AspireStartedAtUtc,
      AspireProcessId: value.AspireProcessId, AspireProcessStartedAtUtc: value.AspireProcessStartedAtUtc,
      StopTimeStamp: '2026-10-09T10:59:00Z', State: 'Exited' })) };
    const originals = [Buffer.from(originalTrx()), Buffer.from(JSON.stringify(started)),
      Buffer.from(JSON.stringify(started)), Buffer.from(JSON.stringify(closed))];
    for (const [index, bytes] of originals.entries()) {
      await mkdir(path.dirname(path.join(source, entries[index])), { recursive: true });
      await writeFile(path.join(source, entries[index]), bytes); await writeFile(path.join(input, FUNCTIONAL.files[index]), bytes);
      proof.files[index].bytes = bytes.length; proof.files[index].sha256 = digest(bytes);
    }
    const archive = path.join(input, FUNCTIONAL.archive);
    const zipped = await runBounded('zip', ['-q', archive, ...entries], { cwd: source });
    if (!zipped.success) throw new Error('Native ZIP fixture failed.');
    const raw = await hashRegularFile(archive, 33554432);
    proof.archive.bytes = raw.bytes; proof.archive.sha256 = raw.sha256;
    proof.artifact.sizeInBytes = raw.bytes; proof.artifact.digest = 'sha256:' + raw.sha256;
    const originalCapture = captureOf(proof);
    await verifyDocumentFunctionalGate(input, proof, originalCapture, cohort, runtimeIdentity, directory);
    const changedProof = structuredClone(proof); changedProof.files[0].sha256 = 'd'.repeat(64);
    const changedProofDigest = await rejected(() => verifyDocumentFunctionalGate(input, changedProof, originalCapture, cohort, runtimeIdentity, directory));
    const wrongServer = await rejected(() => verifyDocumentFunctionalGate(input, proof, originalCapture, cohort, {descriptor: {...runtimeIdentity.descriptor, assemblies: [{...runtimeIdentity.descriptor.assemblies[0], sha256: 'd'.repeat(64)}]}}, directory));
    await writeFile(path.join(input, FUNCTIONAL.files[0]), originalTrx('Failed'));
    const changedOriginal = await rejected(() => verifyDocumentFunctionalGate(input, proof, originalCapture, cohort, runtimeIdentity, directory));
    await writeFile(path.join(input, FUNCTIONAL.files[0]), originals[0]);
    await appendFile(archive, 'changed original ZIP bytes');
    const changedDigest = changedProofDigest && await rejected(() => verifyDocumentFunctionalGate(input, proof, originalCapture, cohort, runtimeIdentity, directory));
    return { passed: positive.count, xmlRejected, captureRejected, completeOriginalPassed: true, changedDigest, wrongServer, changedOriginal };
  } finally { await rm(directory, { recursive: true, force: true }); }
}
if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.stdout.write(JSON.stringify(await probe()));
}
