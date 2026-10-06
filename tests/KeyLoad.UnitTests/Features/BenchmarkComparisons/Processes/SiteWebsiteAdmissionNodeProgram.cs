namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class SiteWebsiteAdmissionNodeProgram
{
    internal const string Source = """
        async function assertWebsiteExecutor(scenario) {
          const { SITE_GH } = contract;
          const revision = 'a'.repeat(40);
          const makeFixture = () => ({ environment: {
            GITHUB_REPOSITORY: SITE_GH.repository, GH_REPO: SITE_GH.repository,
            GITHUB_REPOSITORY_ID: String(SITE_GH.repositoryId), GITHUB_ACTIONS: 'true', RUNNER_OS: 'Linux',
            GITHUB_WORKFLOW: 'Website', GITHUB_WORKFLOW_SHA: revision,
            GITHUB_WORKFLOW_REF: `${SITE_GH.repository}/.github/workflows/website.yml@refs/heads/main`,
            GITHUB_JOB: SITE_GH.executorJobs[0], GITHUB_EVENT_NAME: 'push', GITHUB_SHA: revision,
            GH_TOKEN: 'controlled-test-token', GITHUB_RUN_ID: '1', GITHUB_RUN_ATTEMPT: '1',
            GITHUB_WORKSPACE: '/tmp/keyload-site-tests', GITHUB_REF: 'refs/heads/main',
          }, args: { mode: SITE_GH.publish, 'workflow-revision': revision, 'site-revision': revision } });
          const candidate = makeFixture();
          if (scenario === 'website-executor-legacy-ci') candidate.environment.GITHUB_WORKFLOW = 'CI';
          if (scenario === 'website-executor-wrong-path') {
            candidate.environment.GITHUB_WORKFLOW_REF = `${SITE_GH.repository}/.github/workflows/ci.yml@refs/heads/main`;
          }
          const before = JSON.stringify(candidate);
          let accepted = true;
          try { await context.createSiteIsolatedContext(candidate.environment, candidate.args, 'linux'); }
          catch { accepted = false; }
          assert(accepted === (scenario === 'website-executor-accepted'));
          assert(JSON.stringify(candidate) === before);
          const followup = makeFixture();
          const healthy = await context.createSiteIsolatedContext(followup.environment, followup.args, 'linux');
          assert(healthy.executor.workflow === 'Website' && healthy.executor.sourceRevision === revision
            && healthy.executor.event === 'push' && healthy.source.website === revision && healthy.source.control === revision);
        }
        """;
}
