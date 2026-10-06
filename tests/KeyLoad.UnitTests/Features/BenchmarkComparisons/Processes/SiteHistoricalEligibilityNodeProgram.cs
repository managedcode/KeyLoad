namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class SiteHistoricalEligibilityNodeProgram
{
    internal const string Source = """
        async function assertHistoricalPublicationEligibility(scenario) {
          // These parser inputs retain the original native objects; they do not claim to be a provider capture.
          const snapshot = JSON.stringify({ readyRun, readyJob, workflow });
          const temporary = await mkdtemp(path.join(os.tmpdir(), 'keyload-site-compatibility-'));
          const root = await realpath(temporary);
          try {
            const metadata = path.join(root, 'metadata');
            const attempt = path.join(metadata, 'attempts', String(readyRun.id), String(readyRun.run_attempt));
            await mkdir(attempt, { recursive: true });
            await writeFile(path.join(metadata, 'workflow.json'), JSON.stringify(workflow));
            await writeFile(path.join(metadata, 'workflow_runs-pages.json'), JSON.stringify([
              { total_count: 1, workflow_runs: [readyRun] },
            ]));
            await writeFile(path.join(attempt, 'run-attempt.json'), JSON.stringify(readyRun));
            await writeFile(path.join(attempt, 'jobs-pages.json'), JSON.stringify([
              { total_count: 1, jobs: [readyJob] },
            ]));
            const producer = { runId: readyRun.id, attempt: readyRun.run_attempt,
              sourceRevision: readyRun.head_sha, event: readyRun.event, conclusion: readyRun.conclusion };
            const selection = await runs.selectSiteIsolatedEvidence(scenario === 'retained-live-control-unavailable'
              ? { input: root, mode: 'publish', producer, optional: true }
              : { input: root, mode: 'validate', requestedRun: String(readyRun.id) });
            assert(scenario === 'retained-live-control-unavailable' ? selection.state === 'unavailable'
              : selection.state === 'selected' && selection.job.id === readyJob.id);
            assert(JSON.stringify({ readyRun, readyJob, workflow }) === snapshot);
            assert(runs.selectSiteAggregateJob(readyRun, [readyJob]).successful);
          } finally { await rm(temporary, { recursive: true, force: true }); }
        }
        async function assertUnavailableGeneration(scenario) {
          const label = scenario.startsWith('retained-52-') ? '52' : '48';
          const run = runs.validateSiteRun(await read('SiteOptionalRetained' + label + 'Run.json'), workflow);
          const job = await read('SiteOptionalRetained' + label + 'Aggregate.json');
          const snapshot = JSON.stringify({ run, job });
          const candidate = structuredClone({ run, job });
          if (scenario.endsWith('changed-source')) {
            candidate.run.head_sha = 'c'.repeat(40);
            candidate.job.head_sha = candidate.run.head_sha;
          } else if (scenario.endsWith('changed-step')) {
            candidate.job.steps.find(step => step.name === 'Check all 270 benchmark results').name += ' changed';
          }
          let accepted = true;
          let available = false;
          try { available = runs.selectSiteAggregateJob(candidate.run, [candidate.job]).successful; }
          catch { accepted = false; }
          assert(scenario.endsWith('control-unavailable') ? accepted && !available : !accepted);
          assert(JSON.stringify({ run, job }) === snapshot && !runs.selectSiteAggregateJob(run, [job]).successful);
        }
        """;
}
