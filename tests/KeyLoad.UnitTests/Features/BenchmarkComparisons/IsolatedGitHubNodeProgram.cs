namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedGitHubNodeProgram
{
    internal const string Metadata = """
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const [modulePath, corruption] = process.argv.slice(1);
        const api = await import(pathToFileURL(modulePath));
        const planner = await import(pathToFileURL(path.join(path.dirname(modulePath), 'isolated-plan.mjs')));
        const parser = await import(pathToFileURL(path.join(path.dirname(modulePath), 'aggregate-json.mjs')));
        const plan = planner.createIsolatedPlan();
        const cohort = { sourceRevision: 'a'.repeat(40), runId: 123, attempt: 1, repository: 'managedcode/KeyLoad',
          ref: 'refs/heads/main', workflow: 'Benchmarks', profile: plan.profile };
        const workflow = { id: 1, name: 'Benchmarks', path: '.github/workflows/benchmarks.yml', state: 'active' };
        const run = { id: 123, run_attempt: 1, workflow_id: 1, name: 'Benchmarks', head_sha: cohort.sourceRevision,
          head_branch: 'main', path: '.github/workflows/benchmarks.yml', status: 'in_progress', conclusion: null,
          run_started_at: '2026-10-03T10:00:00Z', repository: { id: 7, full_name: cohort.repository },
          head_repository: { id: 7, full_name: cohort.repository } };
        const cell = plan.cells[0];
        const job = { id: 1001, run_id: 123, run_attempt: 1, head_sha: cohort.sourceRevision,
          name: 'case / ' + cell.id, status: 'completed', conclusion: 'success',
          started_at: '2026-10-03T10:01:00Z', completed_at: '2026-10-03T10:03:00Z',
          html_url: 'https://github.com/managedcode/KeyLoad/actions/runs/123/job/1001',
          steps: [{ name: 'Set up job', number: 1, status: 'completed', conclusion: 'success' },
            { name: 'Run isolated native case', number: 2, status: 'completed', conclusion: 'success' },
            { name: 'Retain isolated worker evidence', number: 3, status: 'completed', conclusion: 'success' }] };
        const artifact = { id: 2001, name: 'comparison-worker-' + cell.id, size_in_bytes: 100, digest: 'sha256:' + 'b'.repeat(64),
          expired: false, created_at: '2026-10-03T10:02:00Z', updated_at: '2026-10-03T10:02:00Z',
          workflow_run: { id: 123, head_sha: cohort.sourceRevision, head_branch: 'main', repository_id: 7, head_repository_id: 7 } };
        const jobs = [{ total_count: 1, jobs: [job] }];
        const artifacts = [{ total_count: 1, artifacts: [artifact] }];
        if (corruption === 'valid-legacy-url') job.html_url = 'https://github.com/managedcode/KeyLoad/runs/123/jobs/1001';
        if (corruption === 'valid-native-no-attempt') delete job.run_attempt;
        if (corruption === 'source') run.head_sha = 'c'.repeat(40);
        if (corruption === 'run') run.id++;
        if (corruption === 'attempt') run.run_attempt++;
        if (corruption === 'repository') run.repository.full_name = 'foreign/repository';
        if (corruption === 'workflow') workflow.path = '.github/workflows/foreign.yml';
        if (corruption === 'branch') run.head_branch = 'foreign';
        if (corruption === 'duplicate-job') { jobs[0].jobs.push(job); jobs[0].total_count++; }
        if (corruption === 'duplicate-artifact') { artifacts[0].artifacts.push(artifact); artifacts[0].total_count++; }
        if (corruption === 'pagination-count') jobs[0].total_count++;
        if (corruption === 'missing-job') { jobs[0].jobs = []; jobs[0].total_count = 0; }
        if (corruption === 'missing-artifact') { artifacts[0].artifacts = []; artifacts[0].total_count = 0; }
        if (corruption === 'foreign-job') job.run_id++;
        if (corruption === 'failed-job') job.conclusion = 'failure';
        if (corruption === 'missing-step') job.steps.pop();
        if (corruption === 'duplicate-step') job.steps.push({ ...job.steps[1], number: 4 });
        if (corruption === 'failed-step') job.steps[1].conclusion = 'failure';
        if (corruption === 'expired-artifact') artifact.expired = true;
        if (corruption === 'artifact-source') artifact.workflow_run.head_sha = 'c'.repeat(40);
        if (corruption === 'artifact-run') artifact.workflow_run.id++;
        if (corruption === 'artifact-repository') artifact.workflow_run.repository_id++;
        if (corruption === 'artifact-time') artifact.created_at = '2026-10-03T09:59:00Z';
        if (corruption === 'artifact-hash') artifact.digest = 'b'.repeat(64);
        try {
          if (corruption === 'duplicate-json') parser.parseBytes(Buffer.from('{"id":1,"id":1}'));
          api.validateWorkflowRun(workflow, run, cohort);
          const parsedJobs = api.flattenPages(jobs, 'jobs');
          const parsedArtifacts = api.flattenPages(artifacts, 'artifacts');
          const selected = api.validateSuccessfulJob(parsedJobs.find(item => item.name === job.name), cohort,
            'case / ' + cell.id, ['Run isolated native case', 'Retain isolated worker evidence']);
          const matched = parsedArtifacts.find(item => item.name === artifact.name);
          api.validateArtifact(matched, run, selected, 'comparison-worker-' + cell.id, 134217728);
          const proof = api.projectWorkerProof(selected, matched, cell, cohort, 'c'.repeat(64));
          if (proof.job.steps.length !== 2 || proof.id !== cell.id || proof.workerSha256 !== 'c'.repeat(64)) throw new Error('projection');
          process.stdout.write('accepted\n');
        } catch { process.stdout.write('rejected\n'); process.exitCode = 1; }
        """;
}
