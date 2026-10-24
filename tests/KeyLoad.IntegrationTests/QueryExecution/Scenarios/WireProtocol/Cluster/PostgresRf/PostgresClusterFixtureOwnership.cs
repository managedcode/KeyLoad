using Aspire.Hosting.Testing;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed class PostgresClusterFixtureOwnership(PostgresClusterSelection? selection = null)
{
    private const int NoFailures = 0;

    internal void Configure(IDistributedApplicationTestingBuilder builder, string root)
        => selection?.Configure(builder, root);

    internal static async Task<bool> ReleaseRootAsync(bool localIncomplete, List<Exception> failures,
        Func<Task> deleteOwnedRoot)
    {
        if (!localIncomplete && failures.Count == NoFailures)
        {
            await deleteOwnedRoot().ConfigureAwait(false);
        }
        return localIncomplete;
    }
}
