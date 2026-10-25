using KeyLoad.Server;
namespace KeyLoad.ClusterTests.StorageReplication;

/// <summary>Preserves original diagnostic failures and deletes files only after joined owner cleanup.</summary>
internal static class ClusterFixtureResourceCleanup
{
    internal static Task SaveDiagnosticsAsync(ClusterFixtureDiagnostics? diagnostics,
        List<Exception> failures, CancellationToken token)
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
