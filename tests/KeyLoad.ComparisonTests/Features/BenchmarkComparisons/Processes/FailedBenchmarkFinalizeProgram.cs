namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class FailedBenchmarkFinalizeProgram
{
    internal const string Source = """
        import assert from 'node:assert/strict';
        import { mkdtemp, mkdir, writeFile, readFile, rm, realpath, symlink, lstat } from 'node:fs/promises';
        import { tmpdir } from 'node:os';
        import path from 'node:path';
        const root = path.resolve('scripts/Features/BenchmarkComparisons');
        const module = name => import('file://' + path.join(root, name + '.mjs'));
        const { finalizeWorker } = await module('finalize-worker');
        const { createIsolatedPlan, readIsolatedContract } = await module('isolated-plan');
        const plan = createIsolatedPlan(), contract = readIsolatedContract(), cell = plan.cells[0];
        const cohort = { sourceRevision: 'a'.repeat(40), runId: 123, attempt: 1, repository: 'managedcode/KeyLoad',
          ref: 'refs/heads/main', workflow: 'Benchmarks', profile: plan.profile };
        const expected = { schemaVersion: 4, worker: { target: cell.target, nodeCount: cell.nodeCount,
          scenario: cell.scenario, profile: cell.profile, ...cohort, jobId: 1001 }, disposition: 'failed',
          reason: 'Benchmark failed; no measurement data is available.', report: null };
        const temporary = await realpath(await mkdtemp(path.join(tmpdir(), 'keyload-finalize-regression-')));
        const directory = async name => { const workspace = path.join(temporary, name); await mkdir(workspace); return workspace; };
        const raw = (workspace, selected = cell) => path.join(workspace, 'artifacts/comparisons/isolated/workers', selected.id, 'worker.json');
        const backup = workspace => path.join(workspace, 'artifacts/comparisons/isolated/failures', cell.id, 'failed-worker.json');
        const options = workspace => ({ workspace, cell, cohort, jobId: 1001, outcome: 'failure' });
        const put = async (target, bytes) => { await mkdir(path.dirname(target), { recursive: true }); await writeFile(target, bytes); };
        try {
          const startup = await directory('startup');
          assert.deepEqual(await finalizeWorker(options(startup)), expected);
          assert.deepEqual(JSON.parse(await readFile(raw(startup), 'utf8')), expected);
          assert.equal((await lstat(backup(startup)).catch(() => null)), null);
          const previous = await directory('previous');
          const original = Buffer.from(' {"disposition":"measured","report":{"unfinished":true}}\n');
          await put(raw(previous), original);
          assert.deepEqual(await finalizeWorker(options(previous)), expected);
          assert.deepEqual(await readFile(backup(previous)), original);
          assert.deepEqual(JSON.parse(await readFile(raw(previous), 'utf8')), expected);
          const absent = await directory('absent');
          await assert.rejects(finalizeWorker({ ...options(absent), outcome: 'success' }));
          await assert.rejects(finalizeWorker({ ...options(startup), outcome: 'success' }));
          const unsupported = contract.unsupportedTopologies[0];
          const selected = plan.cells.find(c => c.target === unsupported.target && unsupported.nodeCounts.includes(c.nodeCount));
          const envelope = { ...expected, worker: { ...expected.worker, target: selected.target,
            nodeCount: selected.nodeCount, scenario: selected.scenario }, disposition: 'unsupportedTopology', reason: unsupported.reason };
          const supported = await directory('unsupported');
          const bytes = Buffer.from(JSON.stringify(envelope)); await put(raw(supported, selected), bytes);
          assert.deepEqual(await finalizeWorker({ ...options(supported), cell: selected, outcome: 'success' }), envelope);
          assert.deepEqual(await readFile(raw(supported, selected)), bytes);
          const invalid = await directory('invalid');
          for (const changes of [{ outcome: 'cancelled' }, { jobId: 0 }, { jobId: 1.5 }, { cell: { ...cell, id: 'foreign-cell' } },
            { cell: { ...cell, id: '../escape' } }, { cell: { ...cell, target: 'foreign' } }]) {
            await assert.rejects(finalizeWorker({ ...options(invalid), ...changes }));
          }
          assert.equal((await lstat(path.join(invalid, 'artifacts')).catch(() => null)), null);
          const duplicate = await directory('duplicate');
          await put(raw(duplicate), original); await put(backup(duplicate), Buffer.from('retained'));
          await assert.rejects(finalizeWorker(options(duplicate)));
          assert.deepEqual(await readFile(raw(duplicate)), original);
          assert.equal(await readFile(backup(duplicate), 'utf8'), 'retained');
          const linked = await directory('linked'), foreign = await directory('foreign');
          await symlink(foreign, path.join(linked, 'artifacts'), 'dir');
          await assert.rejects(finalizeWorker(options(linked)));
          const linkedFile = await directory('linked-file');
          await put(path.join(foreign, 'foreign.json'), original);
          await mkdir(path.dirname(raw(linkedFile)), { recursive: true });
          await symlink(path.join(foreign, 'foreign.json'), raw(linkedFile));
          await assert.rejects(finalizeWorker(options(linkedFile)));
          assert.deepEqual(await readFile(path.join(foreign, 'foreign.json')), original);
        } finally { await rm(temporary, { recursive: true, force: true }); }
        process.stdout.write('accepted\n');
        """;
}
