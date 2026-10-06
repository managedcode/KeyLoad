namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class OpenLoopCohortNodeProgram
{
    internal const string RootEnvironment = "KEYLOAD_OPEN_LOOP_COHORT_ROOT";
    internal const string ModuleEnvironment = "KEYLOAD_OPEN_LOOP_COHORT_MODULE";

    internal const string Source = """
        import path from 'node:path';
        import { mkdir, writeFile } from 'node:fs/promises';
        import { createHash } from 'node:crypto';
        import { pathToFileURL } from 'node:url';
        const root = process.env.KEYLOAD_OPEN_LOOP_COHORT_ROOT;
        const moduleUrl = pathToFileURL(process.env.KEYLOAD_OPEN_LOOP_COHORT_MODULE);
        const { readIsolatedContract } = await import(new URL('./isolated-plan.mjs', moduleUrl));
        const { createOpenLoopPlan } = await import(moduleUrl);
        const { isolatedJobName } = await import(new URL('../../../site/Features/BenchmarkComparisons/isolated-contracts.mjs', moduleUrl));
        const terminalModule = await import(new URL('./open-loop-cell-terminal.mjs', moduleUrl));
        const contract = readIsolatedContract();
        const plan = createOpenLoopPlan(contract);
        const cohort = { sourceRevision: 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', runId: 938_451,
          attempt: 1, repository: 'managedcode/KeyLoad', ref: 'refs/heads/main', workflow: 'Benchmarks' };
        const cells = [...plan.measurementCells, ...plan.cancellationProofCells];
        const intakeCells = [];
        await mkdir(path.join(root, 'cells'), { recursive: true });
        await mkdir(path.join(root, 'archives'), { recursive: true });
        await writeFile(path.join(root, 'open-loop-isolated-plan.v1.json'), JSON.stringify(plan) + '\n', { flag: 'wx' });
        for (const [index, cell] of cells.entries()) {
          const directory = path.join(root, 'cells', cell.id);
          await mkdir(directory, { recursive: false });
          const jobId = 8_000_000 + index;
          const archiveName = (cell.cancellationProof ? 'comparison-open-loop-proof-' : 'comparison-open-loop-worker-') + cell.id;
          const archive = Buffer.from(`untrusted failed-cell tooling fixture ${cell.id}\n`, 'utf8');
          await writeFile(path.join(root, 'archives', archiveName + '.zip'), archive, { flag: 'wx' });
          const worker = { target: cell.target, nodeCount: cell.nodeCount, scenario: cell.scenario,
            profile: cell.profile, ...cohort, jobId };
          const artifact = { id: 9_000_000 + index, name: archiveName, sizeInBytes: archive.length,
            digest: 'sha256:' + createHash('sha256').update(archive).digest('hex'), expired: false };
          const profile = cell.profile === 'intensive-1k-c16' ? '' : ` / ${cell.profile}`;
          const kind = cell.cancellationProof ? 'cancellation proof' : 'open-loop';
          const job = { id: jobId, name: isolatedJobName(cell, false) + profile + ` / ${kind} ${cell.offeredRatePerSecond} ops/s`,
            url: `https://github.com/managedcode/KeyLoad/actions/runs/${cohort.runId}/job/${jobId}`,
            conclusion: 'failure', steps: [{ name: 'Run database workload', conclusion: 'failure' },
              { name: 'Save benchmark results', conclusion: 'success' }] };
          await terminalModule.publishCellTerminal({ directory, plan, cell, worker,
            disposition: 'failed', reason: 'Benchmark failed; no measurement data is available.' });
          intakeCells.push({ id: cell.id, job, artifact });
        }
        await writeFile(path.join(root, 'open-loop-cohort-intake.v1.json'), JSON.stringify({
          schemaVersion: 1, kind: 'open-loop-cohort-intake.v1', cohort, cells: intakeCells }), { flag: 'wx' });
        process.stdout.write(JSON.stringify({ seeded: cells.length }) + '\n');
        """;
}
