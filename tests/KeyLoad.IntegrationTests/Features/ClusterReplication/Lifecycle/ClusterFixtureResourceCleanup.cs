using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.IntegrationTests.Features.CodeQuality;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Preserves the shared fixture diagnostic/root cleanup and actual stage-failure collection semantics.</summary>
internal static class ClusterFixtureResourceCleanup
{
    internal static async Task SaveDiagnosticsAsync(ClusterFixtureDiagnostics? diagnostics, byte[] peerSecret,
        NativeCoverageCleanupDeadline? deadline, List<Exception> failures)
    {
        if (diagnostics is not { } owned)
        {
            return;
        }
        await CollectCleanupAsync(deadline,
            () => owned.SaveAsync(peerSecret, deadline?.Token ?? owned.LifetimeToken), failures).ConfigureAwait(false);
    }

    internal static async Task DeleteOwnedRootAsync(NativeCoverageRf3FixtureOwner? coverage, string root,
        NativeCoverageCleanupDeadline? deadline, List<Exception> failures)
    {
        if (coverage is null)
        {
            if (Directory.Exists(root))
            {
                await ClusterFixtureCleanup.CollectFailureAsync(() => DeleteRootAsync(root), failures).ConfigureAwait(false);
            }
        }
        else if (Directory.Exists(root) && failures.Count == 0)
        {
            await deadline!.CollectAsync(() => Task.Run(() => Directory.Delete(root, recursive: true)), failures)
                .ConfigureAwait(false);
        }
    }

    internal static void Rethrow(Exception failure) => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    private static Task DeleteRootAsync(string root)
    {
        Directory.Delete(root, recursive: true);
        return Task.CompletedTask;
    }

    internal static Task CollectCleanupAsync(NativeCoverageCleanupDeadline? deadline, Func<Task> operation,
        List<Exception> failures)
        => deadline is null
            ? ClusterFixtureCleanup.CollectFailureAsync(operation, failures)
            : deadline.CollectAsync(operation, failures);
    internal static Task CollectCleanupAsync(NativeCoverageCleanupDeadline? deadline, Task operation,
        List<Exception> failures)
        => deadline is null
            ? ClusterFixtureCleanup.CollectFailureAsync(operation, failures)
            : deadline.CollectAsync(operation, failures);
}
