using Aspire.Hosting;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.IntegrationTests.Features.CodeQuality;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ClusterFixtureApplicationStartup
{
    internal static ClusterFixtureColdStartObservation ObserveColdStart(string root)
        => new(root, Directory.Exists(root), File.Exists(root));

    internal static async Task StartAsync(DistributedApplication application, string repository,
        IReadOnlyDictionary<string, string> containerNames, NativeCoverageRf3FixtureOwner? coverage,
        LocalRf3ImageSelection.Selection? selection, CancellationToken token)
    {
        var identity = coverage is null
            ? await ReadLocalIdentityAsync(application, repository, selection, token).ConfigureAwait(false)
            : null;
        if (coverage is null && identity is null)
        {
            await ClusterFixtureImageIdentity.VerifyAsync(application, token).ConfigureAwait(false);
        }
        else if (coverage is not null)
        {
            await coverage.VerifyBeforeStartAsync(application, token).ConfigureAwait(false);
        }
        await application.StartAsync(token).ConfigureAwait(false);
        var readinessNodes = Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount)
            .Select(ClusterFixtureProtocol.NodeName);
        await AspireStartupReadiness.WaitForHealthyAsync(application, readinessNodes, token).ConfigureAwait(false);
        if (identity is not null)
        {
            await LocalRf3ImageIdentity.VerifyStartedContainersAsync(identity, containerNames,
                ClusterFixtureLocalImageLifecycle.ExpectedNames, token).ConfigureAwait(false);
        }
        if (coverage is not null)
        {
            await coverage.VerifyStartedAsync(containerNames, token).ConfigureAwait(false);
        }
    }

    private static async Task<LocalRf3ImageIdentity.Identity?> ReadLocalIdentityAsync(DistributedApplication application,
        string repository, LocalRf3ImageSelection.Selection? selection, CancellationToken token)
        => selection is null
            ? await LocalRf3ImageIdentity.VerifyBeforeStartAsync(application, repository, token).ConfigureAwait(false)
            : await LocalRf3ImageIdentity.VerifyBeforeStartAsync(application, repository, selection,
                ClusterFixtureLocalImageLifecycle.ExpectedNames, token).ConfigureAwait(false);
}
