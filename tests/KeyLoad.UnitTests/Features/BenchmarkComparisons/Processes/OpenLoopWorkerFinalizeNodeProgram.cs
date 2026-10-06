namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class OpenLoopWorkerFinalizeNodeProgram
{
    internal const string Source = """
        import { mkdir, readFile, readdir, unlink, writeFile } from 'node:fs/promises';
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const root = process.argv[2];
        const base = pathToFileURL(process.argv[1]);
        const planModule = await import(new URL('./open-loop-isolated-plan.mjs', base));
        const contractModule = await import(new URL('./isolated-plan.mjs', base));
        const scaledModule = await import(new URL('./scaled-isolated-plan.mjs', base));
        const vectorModule = await import(new URL('./vector-isolated-plan.mjs', base));
        const finalizer = await import(new URL('./open-loop-worker-finalize.mjs', base));
        const plan = planModule.createOpenLoopPlan();
        const cell = plan.measurementCells.find(item => item.target === 'KeyLoad' && item.nodeCount === 3
          && item.scenario === 'PointRead' && item.profile === 'scaled-100k-c16' && item.offeredRatePerSecond === 250);
        const basePlan = contractModule.createIsolatedPlan();
        const scaledPlans = scaledModule.createScaledPlans();
        const vectorPlans = vectorModule.createVectorPlans();
        const matrices = (await import(new URL('./isolated-preflight.mjs', base))).createDatabaseMatrices(
          basePlan, scaledPlans, vectorPlans, plan);
        const row = matrices.keyload.include.find(item => item.id === cell.id);
        const context = { openLoopPlan: plan, plan: basePlan, scaledPlans, vectorPlans,
          native: { sourceSha: 'a'.repeat(40), runId: '123456', runAttempt: '2' },
          cohort: { sourceRevision: 'a'.repeat(40), runId: 123456, attempt: 2,
            repository: 'managedcode/KeyLoad', ref: 'refs/heads/main', workflow: 'Benchmarks', profile: basePlan.profile } };
        const environment = { KEYLOAD_COMPARISON_CELL_ID: cell.id, KEYLOAD_COMPARISON_JOB_NAME: row.jobName,
          KEYLOAD_OPEN_LOOP_RATE: '250', KEYLOAD_OPEN_LOOP_CANCELLATION_PROOF: 'false',
          KEYLOAD_SCALE_PROFILE: cell.profile, Benchmarks__EvidenceProfile: cell.profile,
          Benchmarks__Target: cell.target, Benchmarks__NodeCount: '3', Benchmarks__Scenario: cell.scenario };
        const job = { id: 77, name: row.jobName, run_id: context.cohort.runId,
          run_attempt: context.cohort.attempt, head_sha: context.cohort.sourceRevision };
        const directory = path.join(root, 'artifacts', 'comparisons', 'isolated', 'workers', cell.id);
        await mkdir(directory, { recursive: true });
        const reportPath = path.join(directory, 'open-loop-evidence.v1.json');
        const sidecarPath = path.join(directory, 'open-loop-server-resource-evidence.v1.json');
        const genericWorkerPath = path.join(directory, 'worker.json');
        const genericSidecarPath = path.join(directory, 'server-resource-evidence.json');
        const pendingReportPath = path.join(directory, '.open-loop-evidence.v1.json.pending');
        const partialReport = Buffer.from('partial native report retained after failed workload\n');
        const partialSidecar = Buffer.from('partial native resource snapshot retained\n');
        const partialWorker = Buffer.from('partial generic report retained after failed workload\n');
        const partialGenericSidecar = Buffer.from('partial generic resource snapshot retained\n');
        const partialPendingReport = Buffer.from('pending report writer bytes retained\n');
        await writeFile(reportPath, partialReport, { flag: 'wx' });
        await writeFile(sidecarPath, partialSidecar, { flag: 'wx' });
        await writeFile(genericWorkerPath, partialWorker, { flag: 'wx' });
        await writeFile(genericSidecarPath, partialGenericSidecar, { flag: 'wx' });
        await writeFile(pendingReportPath, partialPendingReport, { flag: 'wx' });
        const failureDirectory = path.join(root, 'artifacts', 'comparisons', 'isolated', 'failures', cell.id);
        await mkdir(failureDirectory, { recursive: true });
        const conflictingTarget = path.join(failureDirectory, 'open-loop-evidence.v1.json');
        const conflictingBytes = Buffer.from('pre-existing failure evidence must not be replaced\n');
        await writeFile(conflictingTarget, conflictingBytes, { flag: 'wx' });
        const wrong = { ...environment, KEYLOAD_OPEN_LOOP_RATE: '1000' };
        let invalidRejected = false;
        try { await finalizer.finalizeCurrentOpenLoopWorker({ workspace: root, context, environment: wrong, job, outcome: 'failure' }); }
        catch (error) { invalidRejected = error.message === 'Isolated GitHub evidence rejected.'; }
        if (!invalidRejected) throw new Error('A mismatched admitted rate was accepted.');
        const terminalPath = path.join(directory, 'open-loop-cell-terminal.v1.json');
        try { await readFile(terminalPath); throw new Error('Invalid selection published a terminal.'); }
        catch (error) { if (error.code !== 'ENOENT') throw error; }
        if (!(await readFile(reportPath)).equals(partialReport) || !(await readFile(sidecarPath)).equals(partialSidecar)
          || !(await readFile(genericWorkerPath)).equals(partialWorker)
          || !(await readFile(genericSidecarPath)).equals(partialGenericSidecar)
          || !(await readFile(pendingReportPath)).equals(partialPendingReport))
          throw new Error('Invalid selection changed original partial output.');
        let collisionRejected = false;
        try { await finalizer.finalizeCurrentOpenLoopWorker({ workspace: root, context, environment, job,
          outcome: 'failure' }); }
        catch (error) { collisionRejected = error.code === 'EEXIST'; }
        if (!collisionRejected || !(await readFile(reportPath)).equals(partialReport)
          || !(await readFile(conflictingTarget)).equals(conflictingBytes))
          throw new Error('A retained-output collision overwrote or removed original evidence.');
        await unlink(conflictingTarget);
        const terminal = await finalizer.finalizeCurrentOpenLoopWorker({ workspace: root, context, environment, job,
          outcome: 'failure' });
        const originalTerminal = await readFile(terminalPath);
        const workerFiles = await readdir(directory);
        const retainedReportPath = path.join(root, 'artifacts', 'comparisons', 'isolated', 'failures', cell.id,
          'open-loop-evidence.v1.json');
        const retainedSidecarPath = path.join(root, 'artifacts', 'comparisons', 'isolated', 'failures', cell.id,
          'open-loop-server-resource-evidence.v1.json');
        const retainedWorkerPath = path.join(root, 'artifacts', 'comparisons', 'isolated', 'failures', cell.id,
          'worker.json');
        const retainedGenericSidecarPath = path.join(root, 'artifacts', 'comparisons', 'isolated', 'failures', cell.id,
          'server-resource-evidence.json');
        const retainedPendingReportPath = path.join(root, 'artifacts', 'comparisons', 'isolated', 'failures', cell.id,
          '.open-loop-evidence.v1.json.pending');
        if (terminal.disposition !== 'failed' || terminal.reason !== 'Benchmark failed; no measurement data is available.'
          || terminal.artifacts.length !== 0 || !originalTerminal.equals(Buffer.from(`${JSON.stringify(terminal)}\n`))) {
          throw new Error('The admitted failure terminal was incomplete.');
        }
        const retainedReport = await readFile(retainedReportPath);
        const retainedSidecar = await readFile(retainedSidecarPath);
        const retainedWorker = await readFile(retainedWorkerPath);
        const retainedGenericSidecar = await readFile(retainedGenericSidecarPath);
        const retainedPendingReport = await readFile(retainedPendingReportPath);
        if (!retainedReport.equals(partialReport) || !retainedSidecar.equals(partialSidecar)
          || !retainedWorker.equals(partialWorker) || !retainedGenericSidecar.equals(partialGenericSidecar)
          || !retainedPendingReport.equals(partialPendingReport))
          throw new Error('Original partial workload evidence changed.');
        if (workerFiles.length !== 1 || workerFiles[0] !== 'open-loop-cell-terminal.v1.json')
          throw new Error('The worker artifact contains failed partial outputs.');
        let reuseRejected = false;
        try { await finalizer.finalizeCurrentOpenLoopWorker({ workspace: root, context, environment, job, outcome: 'failure' }); }
        catch { reuseRejected = true; }
        if (!reuseRejected || !(await readFile(terminalPath)).equals(originalTerminal)
          || !(await readFile(retainedReportPath)).equals(partialReport)
          || !(await readFile(retainedSidecarPath)).equals(partialSidecar)
          || !(await readFile(retainedWorkerPath)).equals(partialWorker)
          || !(await readFile(retainedGenericSidecarPath)).equals(partialGenericSidecar)
          || !(await readFile(retainedPendingReportPath)).equals(partialPendingReport)) {
          throw new Error('Create-only retry changed retained terminal or original evidence.');
        }
        process.stdout.write('open-loop failure terminal and original evidence preserved\n');
        """;
}
