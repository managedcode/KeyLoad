// Controlled contract fixtures exercise parsers and lifecycle ownership; these are never publication measurements.
import { mkdtemp, mkdir, readFile, writeFile, rm, realpath } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { createDocumentPlan, documentMatrixRow, documentJobName, documentProfile, selectedDocumentCell, validateDocumentPlan } from '../../../../../../scripts/Features/BenchmarkComparisons/document-isolated-plan.mjs';
import { documentStatistics, validateDocumentEnvelope, validateDocumentReport, validateDocumentComparableReports, documentUnsupportedReason } from '../../../../../../scripts/Features/BenchmarkComparisons/document-evidence.mjs';
import { finalizeDocumentWorker } from '../../../../../../scripts/Features/BenchmarkComparisons/document-worker-finalize.mjs';
import { createDatabaseMatrices } from '../../../../../../scripts/Features/BenchmarkComparisons/isolated-preflight.mjs';
import { createIsolatedPlan } from '../../../../../../scripts/Features/BenchmarkComparisons/isolated-plan.mjs';
import { createScaledPlans } from '../../../../../../scripts/Features/BenchmarkComparisons/scaled-isolated-plan.mjs';
import { createVectorPlans } from '../../../../../../scripts/Features/BenchmarkComparisons/vector-isolated-plan.mjs';
import { createOpenLoopPlan } from '../../../../../../scripts/Features/BenchmarkComparisons/open-loop-isolated-plan.mjs';
import { controlledFunctionalProof } from './document-functional-probe.mjs';
const reject = action => { try { action(); return false; } catch { return true; } };
const plan = createDocumentPlan();
const cell = plan.cells.find(item => item.target === 'KeyLoad' && item.nodeCount === 3 && item.documentScenario === 'RandomRead' && item.documentRecords === 100000);
const cohort = { sourceRevision: 'a'.repeat(40), runId: 123, attempt: 1, repository: 'managedcode/KeyLoad', ref: 'refs/heads/main', workflow: 'Benchmarks', profile: cell.profile };
const environment = value => ({ KEYLOAD_MATRIX_KIND: 'documents', KEYLOAD_COMPARISON_CELL_ID: value.id,
  KEYLOAD_COMPARISON_JOB_NAME: documentJobName(value), Benchmarks__Target: value.target, Benchmarks__NodeCount: String(value.nodeCount),
  Benchmarks__Scenario: 'PointRead', Benchmarks__EvidenceProfile: value.profile, Benchmarks__DocumentScenario: value.documentScenario,
  Benchmarks__DocumentRecords: String(value.documentRecords), Benchmarks__DocumentClients: String(value.documentClients) });
function report(value) {
  const initial = value.documentScenario === 'Ingest' ? 0 : value.documentRecords;
  const planned = value.documentScenario === 'Ingest' ? 1000000 : 100000;
  const added = ['Ingest', 'Create'].includes(value.documentScenario) ? planned : value.documentScenario === 'MixedCrud' ? 25000 : 0;
  const deleted = value.documentScenario === 'Delete' ? 100000 : value.documentScenario === 'MixedCrud' ? 25000 : 0;
  return { schemaVersion: 1, family: 'document-v1', selection: { scenario: value.documentScenario, datasetRecords: value.documentRecords, clients: value.documentClients },
    status: 'measured', qualified: true, repetitions: [0, 1, 2].map(repetition => ({ repetition,
      target: { name: value.target, version: 'controlled', topology: value.nodeCount === 1 ? 'Single' : 'Replicated',
        writeAcknowledgement: 'controlled', readContract: 'controlled', transport: 'controlled', authorization: 'controlled',
        image: 'controlled@sha256:' + 'b'.repeat(64), cluster: { nodes: value.nodeCount, dataCopies: value.nodeCount, state: 'controlled', observations: ['controlled'] } },
      status: 'measured', qualified: true, errors: [], initialRecords: initial, addedRecords: added, deletedRecords: deleted,
      expectedFinalRecords: initial + added - deleted, actualFinalRecords: initial + added - deleted, expectedSha256: 'c'.repeat(64), actualSha256: 'c'.repeat(64),
      requestedClients: value.documentClients, openedClients: value.documentClients, peakInFlight: value.documentClients,
      planned, attempts: planned, acknowledged: planned, failed: 0, canceled: 0, unfinished: 0, singleOperationTiming: true,
      phases: { setupSeconds: 1, warmupSeconds: 1, measuredSeconds: 1, verificationSeconds: 1, cleanupSeconds: 1,
        loadSeconds: .5, indexBuildSeconds: .25, indexBuildApplicable: true, phaseInstrumentationComplete: true },
      histogram: { resolutionMicroseconds: 10, maximumMilliseconds: 30000, count: planned, overflowCount: 0,
        bucketIndices: [0, 100, 200], bucketCounts: [planned / 2, planned * .45, planned * .05],
        p50Milliseconds: 0, p95Milliseconds: 1, p99Milliseconds: 2, maximumObservedMilliseconds: 1.999 },
      clientResources: { cpuSeconds: 1, allocatedBytes: 1, peakObservedWorkingSetBytes: 1, samplingIntervalMs: 1 } })) };
}
const envelope = value => ({ schemaVersion: 1, kind: 'document-worker.v1', worker: { target: value.target, nodeCount: value.nodeCount,
  scenario: 'PointRead', profile: value.profile, ...cohort, jobId: 456 }, document: { scenario: value.documentScenario, records: value.documentRecords, clients: value.documentClients },
  disposition: 'measured', reason: null, report: report(value) });
let result;
if (process.argv[2] === 'plan') {
  const matrices = createDatabaseMatrices(createIsolatedPlan(), createScaledPlans(), createVectorPlans(), createOpenLoopPlan(), plan.cells.map(documentMatrixRow));
  const mutate = change => { const copy = structuredClone(plan); change(copy); return reject(() => validateDocumentPlan(copy)); };
  result = { cells: plan.cells.length, targets: new Set(plan.cells.map(item => item.target)).size,
    nodes: [...new Set(plan.cells.map(item => item.nodeCount))], perTarget: [...new Set(plan.cells.map(item => item.target))].map(target => plan.cells.filter(item => item.target === target).length),
    ingestionClients: [...new Set(plan.cells.filter(item => item.documentScenario === 'Ingest').map(item => item.documentClients))],
    groupCounts: Object.values(matrices).map(matrix => matrix.include.length),
    profiles: [documentProfile('ReadUpdate50', 100000, 16), documentProfile('ReadUpdate95', 1000000, 16)],
    selected: selectedDocumentCell(environment(cell)).id === cell.id,
    rejected: [mutate(copy => copy.cells.pop()), mutate(copy => copy.cells[0].nodeCount = 2), mutate(copy => copy.contract.datasetSizes.push(5000000)),
      reject(() => selectedDocumentCell({ ...environment(cell), Benchmarks__DocumentClients: '10' })),
      reject(() => documentProfile('Ingest', 100000, 500)), reject(() => createDatabaseMatrices(createIsolatedPlan(), createScaledPlans(), createVectorPlans(), undefined, [documentMatrixRow(cell)]))] };
} else if (process.argv[2] === 'evidence') {
  const supported = plan.cells.filter(item => item.target === 'KeyLoad');
  for (const value of supported) validateDocumentReport(report(value), value);
  const measured = envelope(cell); validateDocumentEnvelope(measured, cell, cohort);
  const mutations = [copy => copy.repetitions.pop(), copy => copy.repetitions[0].actualSha256 = 'd'.repeat(64),
    copy => copy.repetitions[0].histogram.p95Milliseconds = .5, copy => copy.repetitions[0].histogram.bucketCounts[0]--,
    copy => copy.repetitions[0].histogram.overflowCount = 1, copy => copy.repetitions[0].openedClients--,
    copy => copy.repetitions[0].actualFinalRecords--, copy => copy.repetitions[0].unfinished = 1,
    copy => copy.repetitions[0].clientResources = null, copy => copy.repetitions[0].singleOperationTiming = false,
    copy => copy.repetitions[0].phases.loadSeconds = null, copy => copy.repetitions[0].phases.phaseInstrumentationComplete = false,
    copy => { copy.repetitions[0].phases.indexBuildApplicable = false; copy.repetitions[0].phases.indexBuildSeconds = 1; }];
  const unavailable = plan.cells.filter(value => ['Qdrant', 'Neo4j'].includes(value.target)).map(value => {
    const unsupported = documentUnsupportedReason(value); if (unsupported === null) return true;
    const bound = { ...cohort, profile: value.profile }; const original = envelope(value);
    original.worker = { ...original.worker, ...bound }; Object.assign(original, unsupported, { report: null });
    validateDocumentEnvelope(original, value, bound); return true;
  });
  const comparable = supported.map(value => ({ cell: value, disposition: 'measured', report: report(value) }));
  validateDocumentComparableReports([...comparable, { disposition: 'failed' }]);
  comparable[19].report.repetitions[0].expectedSha256 = 'd'.repeat(64);
  comparable[19].report.repetitions[0].actualSha256 = 'd'.repeat(64);
  const originalStatistics = documentStatistics(report(cell), cell);
  const varied = report(cell); varied.repetitions[1].phases.measuredSeconds = 2; varied.repetitions[2].phases.measuredSeconds = 4;
  const variedStatistics = documentStatistics(varied, cell);
  result = { statistics: originalStatistics, variation: variedStatistics,
    comparableRejected: reject(() => validateDocumentComparableReports(comparable)), supported: supported.length, unavailable: unavailable.every(Boolean),
    rejected: mutations.map(change => { const copy = report(cell); change(copy); return reject(() => validateDocumentReport(copy, cell)); }),
    envelopeRejected: reject(() => validateDocumentEnvelope({ ...measured, document: { ...measured.document, records: 1000000 } }, cell, cohort)) };
} else if (process.argv[2] === 'intake') {
  const { validateDocumentIntake } = await import('../../../../../../scripts/Features/BenchmarkComparisons/document-aggregate-cli.mjs');
  const intake = { schemaVersion: 1, family: 'document-v1', cohort, functionalGate: controlledFunctionalProof(cohort), cells: plan.cells.map((value, index) => ({ cell: value,
    rawPath: `workers/${value.id}/document-worker.json`, resourcePath: `workers/${value.id}/server-resource-evidence.json`,
    proof: { id: value.id, workerSha256: 'a'.repeat(64), job: { id: index + 1, name: documentJobName(value),
      url: `https://github.com/managedcode/KeyLoad/actions/runs/123/job/${index + 1}`, conclusion: 'failure',
      steps: [{ name: 'Run database workload', conclusion: 'failure' }, { name: 'Save benchmark results', conclusion: 'success' }] },
      artifact: { id: index + 1000, name: 'comparison-document-worker-' + value.id, sizeInBytes: 1, digest: 'sha256:' + 'b'.repeat(64), expired: false },
      serverResource: null } })) };
  validateDocumentIntake(intake, plan);
  const mutations = [copy => copy.cells.pop(), copy => copy.cells[0].cell.nodeCount = 2,
    copy => copy.cells[0].rawPath = '../foreign.json', copy => copy.cells[1].proof.job.id = copy.cells[0].proof.job.id,
    copy => copy.cells[0].proof.artifact.expired = true, copy => copy.cells[0].proof.job.steps[1].conclusion = 'failure',
    copy => copy.cells[0].proof.job.name = 'Foreign / Documents / job', copy => copy.cells[0].proof.workerSha256 = 'invalid',
    copy => copy.cells[0].proof.artifact.name = 'comparison-worker-' + copy.cells[0].cell.id];
  result = { cells: intake.cells.length, rejected: mutations.map(change => { const copy = structuredClone(intake); change(copy); return reject(() => validateDocumentIntake(copy, plan)); }) };
} else if (process.argv[2] === 'finalizer') {
  const workspace = await realpath(await mkdtemp(path.join(os.tmpdir(), 'keyload-document-contract-')));
  try {
    const directory = path.join(workspace, 'artifacts/comparisons/isolated/workers', cell.id); await mkdir(directory, { recursive: true });
    const file = path.join(directory, 'document-worker.json'); const bytes = JSON.stringify(envelope(cell)); await writeFile(file, bytes);
    await finalizeDocumentWorker({ workspace, environment: environment(cell), cohort, jobId: 456, outcome: 'success' });
    const unchanged = await readFile(file, 'utf8') === bytes;
    await writeFile(path.join(directory, 'server-resource-evidence.json'), '{}');
    const failed = await finalizeDocumentWorker({ workspace, environment: environment(cell), cohort, jobId: 456, outcome: 'failure' });
    const retained = await readFile(path.join(workspace, 'artifacts/comparisons/isolated/failures', cell.id, 'failed-document-worker.json'), 'utf8') === bytes;
    const resource = JSON.parse(await readFile(path.join(directory, 'server-resource-evidence.json'), 'utf8'));
    let duplicateRejected = false;
    try { await finalizeDocumentWorker({ workspace, environment: environment(cell), cohort, jobId: 456, outcome: 'failure' }); } catch { duplicateRejected = true; }
    result = { unchanged, retained, failed: failed.disposition === 'failed' && failed.report === null, resourceFailed: resource.qualified === false, duplicateRejected };
  } finally { await rm(workspace, { recursive: true, force: true }); }
} else throw new Error('Unknown controlled probe.');
process.stdout.write(JSON.stringify(result));
