using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed class PostgresRf3FixtureOwnership(PostgresRf3Selection? selection = null)
{
    private bool cleanupIncomplete;

    internal string[] Prepare(string root, string[] arguments)
        => selection?.Prepare(root, arguments) ?? arguments;

    internal void Configure(IDistributedApplicationTestingBuilder builder, string root)
        => selection?.Configure(builder, root);

    internal async Task<bool> JoinAndReleaseRootAsync(ClusterFixtureSnapshotReadiness? readiness,
        bool appStopped, bool localIncomplete, bool isolationIncomplete, List<Exception> failures,
        Func<Task> deleteOwnedRoot)
    {
        if (readiness is not null)
        {
            await readiness.JoinAfterStopAsync(appStopped, failures).ConfigureAwait(false);
            localIncomplete |= failures.Count > PostgresRf3Protocol.FirstColumn || !appStopped;
        }
        if (selection is not null)
        {
            await selection.JoinAfterResourcesAsync(appStopped, failures).ConfigureAwait(false);
            cleanupIncomplete |= selection.RetainRoot || failures.Count > PostgresRf3Protocol.FirstColumn || !appStopped;
        }
        if (!localIncomplete && !isolationIncomplete && !cleanupIncomplete)
        {
            await deleteOwnedRoot().ConfigureAwait(false);
        }
        return localIncomplete;
    }
}
