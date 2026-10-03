namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class FailedBenchmarkProducerProgram
{
    internal const string Source = """
        import assert from 'node:assert/strict';
        import { mkdtemp, mkdir, writeFile, rm, realpath } from 'node:fs/promises';
        import { tmpdir } from 'node:os';
        import path from 'node:path';
        import { createHash } from 'node:crypto';
        const root = path.resolve('scripts/Features/BenchmarkComparisons');
        const module = name => import('file://' + path.join(root, name + '.mjs'));
        const { createIsolatedPlan, readIsolatedContract } = await module('isolated-plan');
        const { selectCompletedEvidence } = await module('isolated-github-selection');
        const { projectWorkerProof } = await module('isolated-github-validation');
        const { validateWorkerEnvelope, requireWorkerJobAgreement } = await module('aggregate-validation');
        const { validateAggregateProof } = await module('aggregate-proof');
        const { aggregateEvidence } = await module('aggregate-evidence');
        const { AGGREGATE } = await module('aggregate-contracts');
        const plan = createIsolatedPlan();
        const cohort = { sourceRevision: 'a'.repeat(40), runId: 123, attempt: 1, repository: 'managedcode/KeyLoad',
          ref: 'refs/heads/main', workflow: 'Benchmarks', profile: plan.profile };
        const run = { id: 123, head_sha: cohort.sourceRevision, head_branch: 'main',
          run_started_at: '2026-10-03T10:00:00Z', repository: { id: 7 }, head_repository: { id: 7 } };
        const job = (id, name, names, conclusion) => ({ id, name, run_id: 123, run_attempt: 1,
          head_sha: cohort.sourceRevision, status: 'completed', conclusion,
          started_at: '2026-10-03T10:01:00Z', completed_at: '2026-10-03T10:03:00Z',
          html_url: `https://github.com/managedcode/KeyLoad/actions/runs/123/job/${id}`,
          steps: names.map((name, i) => ({ name, number: i + 1, status: 'completed',
            conclusion: i === 0 ? conclusion : 'success' })) });
        const artifact = (id, name) => ({ id, name, size_in_bytes: 100, expired: false,
          digest: 'sha256:' + 'b'.repeat(64), created_at: '2026-10-03T10:02:00Z',
          workflow_run: { id: 123, head_sha: cohort.sourceRevision, head_branch: 'main',
            repository_id: 7, head_repository_id: 7 } });
        const jobs = plan.cells.map((cell, i) => job(1001 + i, 'Benchmark / ' + cell.id,
          ['Run database workload', 'Save benchmark results'], 'failure'));
        jobs.push(job(5000, 'Build Docker images', ['Check Docker image export and import', 'Save Docker images'], 'success'));
        const artifacts = plan.cells.map((cell, i) => artifact(2001 + i, 'comparison-worker-' + cell.id));
        artifacts.push(artifact(6000, 'comparison-image-bundle'));
        const capture = { run, jobs, artifacts };
        const selected = selectCompletedEvidence(capture, { cohort }, plan);
        assert.equal(selected.cells.length, 270);
        const original = structuredClone(jobs[0]);
        for (const mutate of [j => j.conclusion = 'cancelled', j => j.steps[0].conclusion = 'success',
          j => j.steps[1].conclusion = 'failure', j => j.status = 'in_progress']) {
          jobs[0] = structuredClone(original);
          mutate(jobs[0]); assert.throws(() => selectCompletedEvidence(capture, { cohort }, plan));
          jobs[0] = structuredClone(original);
        }
        artifacts[0].expired = true;
        assert.throws(() => selectCompletedEvidence(capture, { cohort }, plan));
        artifacts[0].expired = false;
        assert.throws(() => selectCompletedEvidence({ ...capture, artifacts: artifacts.slice(1) }, { cohort }, plan));
        const contract = readIsolatedContract();
        const unsupported = contract.unsupportedTopologies[0];
        const unsupportedCell = plan.cells.find(c => c.target === unsupported.target && unsupported.nodeCounts.includes(c.nodeCount));
        const unsupportedEnvelope = { schemaVersion: 4, worker: { target: unsupportedCell.target,
          nodeCount: unsupportedCell.nodeCount, scenario: unsupportedCell.scenario, ...cohort, jobId: 1001 },
          disposition: 'unsupportedTopology', reason: unsupported.reason, report: null };
        validateWorkerEnvelope(unsupportedEnvelope, unsupportedCell, cohort, contract);
        requireWorkerJobAgreement(unsupportedEnvelope, { conclusion: 'success' });
        assert.throws(() => requireWorkerJobAgreement(unsupportedEnvelope, { conclusion: 'failure' }));
        const temporary = await realpath(await mkdtemp(path.join(tmpdir(), 'keyload-failed-producer-')));
        try {
          const input = path.join(temporary, 'input'); await mkdir(input);
          const cells = [];
          for (const item of selected.cells) {
            const envelope = { schemaVersion: 4, worker: { target: item.cell.target, nodeCount: item.cell.nodeCount,
              scenario: item.cell.scenario, ...cohort, jobId: item.job.id }, disposition: 'failed',
              reason: AGGREGATE.failureReason, report: null };
            validateWorkerEnvelope(envelope, item.cell, cohort, readIsolatedContract());
            requireWorkerJobAgreement(envelope, item.job);
            assert.throws(() => requireWorkerJobAgreement(envelope, { ...item.job, conclusion: 'success' }));
            assert.throws(() => validateWorkerEnvelope({ ...envelope, reason: 'secret exception' }, item.cell, cohort, readIsolatedContract()));
            const bytes = Buffer.from(JSON.stringify(envelope));
            const directory = path.join(input, 'workers', item.cell.id); await mkdir(directory, { recursive: true });
            await writeFile(path.join(directory, 'worker.json'), bytes);
            cells.push(projectWorkerProof(item.job, item.artifact, item.cell, cohort,
              createHash('sha256').update(bytes).digest('hex')));
          }
          const planPath = path.join(temporary, 'plan.json'), proofPath = path.join(temporary, 'proof.json');
          await writeFile(planPath, JSON.stringify(plan));
          const proof = { schemaVersion: 1, cohort, cells };
          await writeFile(proofPath, JSON.stringify(proof));
          const result = await aggregateEvidence({ input, output: path.join(temporary, 'output'), plan: planPath, proof: proofPath });
          assert.equal(result.datasetSha256, null);
          assert.equal(result.workers.length, 270);
          assert.ok(result.workers.every(w => w.disposition === 'failed' && w.job.conclusion === 'failure'
            && w.job.steps[0].conclusion === 'failure' && w.job.steps[1].conclusion === 'success'));
          const corrupt = structuredClone(proof);
          corrupt.cells[0].job.steps[1].conclusion = 'skipped';
          assert.throws(() => validateAggregateProof(corrupt, plan));
          cells[0].workerSha256 = 'd'.repeat(64);
          await writeFile(proofPath, JSON.stringify(proof));
          await assert.rejects(aggregateEvidence({ input, output: path.join(temporary, 'hash-rejected'), plan: planPath, proof: proofPath }));
          cells[0].workerSha256 = result.workers[0].rawSha256;
          cells[0].job.conclusion = 'success'; cells[0].job.steps[0].conclusion = 'success';
          await writeFile(proofPath, JSON.stringify(proof));
          await assert.rejects(aggregateEvidence({ input, output: path.join(temporary, 'rejected'), plan: planPath, proof: proofPath }));
        } finally { await rm(temporary, { recursive: true, force: true }); }
        process.stdout.write('accepted\n');
        """;
}
