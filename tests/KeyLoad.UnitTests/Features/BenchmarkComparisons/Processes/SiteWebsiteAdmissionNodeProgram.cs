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
          }, args: { mode: SITE_GH.publish, 'workflow-revision': revision, 'site-revision': revision, optional: 'true' } });
          const candidate = makeFixture();
          if (scenario === 'website-executor-manual') candidate.environment.GITHUB_EVENT_NAME = 'workflow_dispatch';
          if (scenario === 'website-executor-retired-event') candidate.environment.GITHUB_EVENT_NAME = 'workflow_run';
          if (scenario === 'website-executor-ci') {
            candidate.environment.GITHUB_WORKFLOW = 'Build and Tests';
            candidate.environment.GITHUB_WORKFLOW_REF = `${SITE_GH.repository}/.github/workflows/build-and-tests.yml@refs/heads/main`;
            candidate.environment.GITHUB_JOB = 'unit';
          }
          if (scenario === 'website-executor-foreign-path') {
            candidate.environment.GITHUB_WORKFLOW_REF = `${SITE_GH.repository}/.github/workflows/not-website.yml@refs/heads/main`;
          }
          if (scenario === 'website-executor-unsupported-job') candidate.environment.GITHUB_JOB = 'unit';
          const before = JSON.stringify(candidate);
          let rejected = false;
          let admitted;
          try { admitted = await context.createSiteIsolatedContext(candidate.environment, candidate.args, 'linux'); }
          catch (error) {
            if (!(error instanceof Error) || error.message !== SITE_GH.failure) throw error;
            rejected = true;
          }
          const accepted = !rejected;
          assert(accepted === (scenario === 'website-executor-push' || scenario === 'website-executor-manual'));
          if (accepted) assert(admitted.executor.event === candidate.environment.GITHUB_EVENT_NAME && admitted.trigger === null && admitted.optional);
          assert(JSON.stringify(candidate) === before);
          const followup = makeFixture();
          const healthy = await context.createSiteIsolatedContext(followup.environment, followup.args, 'linux');
          assert(healthy.executor.workflow === 'Website' && healthy.executor.sourceRevision === revision
            && healthy.executor.event === 'push' && healthy.optional && healthy.source.website === revision && healthy.source.control === revision);
          return accepted ? 'accepted' : 'rejected';
        }
        """;
}
