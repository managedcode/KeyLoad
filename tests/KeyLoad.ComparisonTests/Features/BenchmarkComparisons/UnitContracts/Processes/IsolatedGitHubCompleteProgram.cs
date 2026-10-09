namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedGitHubCompleteProgram
{
    internal const string Source = """
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const [modulePath, corruption] = process.argv.slice(1);
        const api = await import(pathToFileURL(modulePath));
        const planner = await import(pathToFileURL(path.join(path.dirname(modulePath), 'isolated-plan.mjs')));
        const preflight = await import(pathToFileURL(path.join(path.dirname(modulePath), 'isolated-preflight.mjs')));
        const names = await import(pathToFileURL(path.join(path.dirname(modulePath), 'isolated-github-contract.mjs')));
        const validation = await import(pathToFileURL(path.join(path.dirname(modulePath), 'isolated-github-validation.mjs')));
        const plan = planner.createIsolatedPlan();
        const cohort = { sourceRevision: 'a'.repeat(40), runId: 123, attempt: 1, repository: 'managedcode/KeyLoad',
          ref: 'refs/heads/main', workflow: 'Benchmarks', profile: plan.profile };
        const run = { id: 123, head_sha: cohort.sourceRevision, head_branch: 'main', run_started_at: '2026-10-03T10:00:00Z',
          repository: { id: 7 }, head_repository: { id: 7 } };
        const makeJob = (id, name, steps) => ({ id, run_id: 123, run_attempt: 1, head_sha: cohort.sourceRevision, name,
          status: 'completed', conclusion: 'success', started_at: '2026-10-03T10:01:00Z', completed_at: '2026-10-03T10:03:00Z',
          html_url: `https://github.com/managedcode/KeyLoad/actions/runs/123/job/${id}`,
          steps: steps.map((name, index) => ({ name, number: index + 1, status: 'completed', conclusion: 'success' })) });
        const makeArtifact = (id, name) => ({ id, name, size_in_bytes: 100, digest: 'sha256:' + 'b'.repeat(64), expired: false,
          created_at: '2026-10-03T10:02:00Z', workflow_run: { id: 123, head_sha: cohort.sourceRevision,
            head_branch: 'main', repository_id: 7, head_repository_id: 7 } });
        const modern = corruption.startsWith('modern');
        const jobs = plan.cells.map((cell, i) => makeJob(1001 + i,
          modern ? names.isolatedJobName(cell) : 'Benchmark / ' + cell.id,
          ['Run database workload', 'Save benchmark results']));
        const artifacts = plan.cells.map((cell, i) => makeArtifact(2001 + i, 'comparison-worker-' + cell.id));
        const image = makeJob(5000, 'Build Docker images', ['Check Docker image export and import', 'Save Docker images']);
        const imageArtifact = makeArtifact(6000, 'comparison-image-bundle');
        jobs.push(image); artifacts.push(imageArtifact);
        if (modern) for (const [i, cell] of preflight.createPreflightMatrix(plan).include.entries()) {
          jobs.push(makeJob(7001 + i, names.isolatedJobName(cell, true), ['Run database workload', 'Save benchmark results']));
          artifacts.push(makeArtifact(8001 + i, 'comparison-preflight-' + cell.id));
        }
        if (corruption === 'modern-failed') { jobs[0].conclusion = 'failure'; jobs[0].steps[0].conclusion = 'failure'; }
        if (corruption === 'modern-duplicate') jobs[1].name = jobs[0].name;
        if (corruption === 'modern-foreign-name') jobs[0].name = 'KeyLoad / 1 node / Foreign workload';
        if (corruption === 'modern-preflight-replaces-worker') jobs[0].name = names.isolatedJobName(plan.cells[0], true);
        if (corruption === 'modern-mixed') jobs[0].name = 'Benchmark / ' + plan.cells[0].id;
        if (corruption === 'missing-cell-job') jobs.shift();
        if (corruption === 'missing-cell-artifact') artifacts.shift();
        if (corruption === 'duplicate-case-name') jobs[1].name = jobs[0].name;
        if (corruption === 'unknown-case') jobs[0].name = 'Benchmark / foreign-n1-point-read';
        if (corruption === 'unknown-worker') artifacts[0].name = 'comparison-worker-foreign-n1-point-read';
        if (corruption === 'image-job-failed') image.conclusion = 'failure';
        if (corruption === 'image-step-missing') image.steps.pop();
        if (corruption === 'image-artifact-expired') imageArtifact.expired = true;
        if (corruption === 'zip-total') for (const artifact of artifacts.slice(0, -1)) artifact.size_in_bytes = 134217728;
        try {
          const selected = api.selectCompletedEvidence({run, jobs, artifacts}, {cohort}, plan);
          const cells = selected.cells.map(item => validation.projectWorkerProof(item.job, item.artifact, item.cell, cohort, 'c'.repeat(64)));
          const proof = api.requireCompleteProof({schemaVersion:1,cohort,cells}, plan);
          if (proof.cells.length !== 220 || proof.cells.some(cell => cell.job.steps.length !== 2)) throw new Error('projection');
          if (modern && proof.cells.some(item => item.job.name.includes(' / Check / '))) throw new Error('preflight leakage');
          if (corruption === 'modern-failed' && (proof.cells[0].job.conclusion !== 'failure'
            || proof.cells[0].job.steps[0].conclusion !== 'failure'
            || proof.cells[0].job.steps[1].conclusion !== 'success')) throw new Error('lost failure');
          process.stdout.write('accepted\n');
        } catch { process.stdout.write('rejected\n'); process.exitCode = 1; }
        """;
}
