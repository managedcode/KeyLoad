using System.Runtime.ExceptionServices;

namespace KeyLoad.IntegrationTests.Features.TestInfrastructure;

internal static class ClusterFixtureStartupFailure
{
    internal static async Task DisposeAndThrowAsync(IAsyncDisposable fixture, Exception startupFailure)
    {
        var cleanup = DisposeAsync(fixture);
        await cleanup.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (cleanup.IsFaulted)
        {
            throw new AggregateException("Cluster fixture startup and cleanup failed.",
                new[] { startupFailure }.Concat(cleanup.Exception!.Flatten().InnerExceptions));
        }
        if (cleanup.IsCanceled)
        {
            try { await cleanup.ConfigureAwait(false); }
            catch (OperationCanceledException cleanupFailure)
            {
                throw new AggregateException("Cluster fixture startup and cleanup failed.", startupFailure, cleanupFailure);
            }
        }
        ExceptionDispatchInfo.Capture(startupFailure).Throw();
    }

    private static async Task DisposeAsync(IAsyncDisposable fixture)
        => await fixture.DisposeAsync().ConfigureAwait(false);
}
