namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedCurrentJobNodeProgram
{
    internal const string Policy = """
        import { readFile } from 'node:fs/promises';
        import { pathToFileURL } from 'node:url';
        const [modulePath, fixturePath, corruption] = process.argv.slice(1);
        const api = await import(pathToFileURL(modulePath));
        const job = JSON.parse(await readFile(fixturePath, 'utf8'));
        const cohort = { sourceRevision: 'dbd01269e5a1e526c3773b213b201950300f1df6', runId: 37120639751,
          attempt: 1, repository: 'managedcode/KeyLoad', ref: 'refs/heads/main', workflow: 'Benchmarks' };
        const name = 'Benchmark / rabbitmq-n2-document-delete';
        if (corruption === 'running' || corruption === 'legacy-running' || corruption === 'missing-attempt-running') {
          job.status = 'in_progress';
        }
        if (corruption === 'legacy-url' || corruption === 'legacy-running') {
          job.html_url = 'https://github.com/managedcode/KeyLoad/runs/37120639751/jobs/111196796054';
        }
        if (corruption === 'missing-attempt' || corruption === 'missing-attempt-running') delete job.run_attempt;
        if (corruption === 'wrong-id') {
          job.id++;
          job.html_url = `https://github.com/managedcode/KeyLoad/actions/runs/37120639751/job/${job.id}`;
        }
        if (corruption === 'wrong-run') job.run_id++;
        if (corruption === 'wrong-attempt') job.run_attempt++;
        if (corruption === 'wrong-source') job.head_sha = 'c'.repeat(40);
        if (corruption === 'wrong-name') job.name = 'Benchmark / another-cell';
        if (corruption === 'wrong-url') job.html_url = 'https://github.com/foreign/repository/actions/runs/37120639751/job/111196796054';
        if (corruption === 'wrong-workflow') job.workflow_name = 'CI';
        if (corruption === 'wrong-branch') job.head_branch = 'release';
        if (corruption === 'terminal') { job.status = 'completed'; job.conclusion = 'failure'; }
        if (corruption === 'unknown-state') job.status = 'mystery';
        if (corruption === 'queued-conclusion') job.conclusion = 'success';
        if (corruption === 'running-conclusion') { job.status = 'in_progress'; job.conclusion = 'success'; }
        if (corruption === 'malformed') delete job.started_at;
        try {
          const state = api.classifyCurrentJob(job, cohort, name, 111196796054);
          process.stdout.write(state + '\n');
        } catch {
          process.stdout.write('rejected\n');
          process.exitCode = 1;
        }
        """;
}
