using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Preserves original diagnostic failures and deletes files only after joined owner cleanup.</summary>
internal static class ClusterFixtureResourceCleanup
{
    internal static Task SaveDiagnosticsAsync(ClusterFixtureDiagnostics? diagnostics,
        CancellationToken token, List<Exception> failures)
        => diagnostics is { } owned
            ? ServerFailureObserver.ObserveAsync(() => owned.SaveAsync(token), failures)
            : Task.CompletedTask;

    internal static Task DeleteOwnedRootAsync(string root, List<Exception> failures)
        => ServerFailureObserver.ObserveAsync(() =>
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
            return Task.CompletedTask;
        }, failures);
}
