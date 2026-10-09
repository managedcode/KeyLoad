using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Transfers the actual Aspire-discovered development HTTP client to its native target.</summary>
internal sealed class DocumentNativeTargetAcquisition : IAsyncDisposable
{
    private HttpClient? client;
    private IComparisonTarget? target;
    private Exception? primary;
    internal static async Task<IComparisonTarget> OpenAsync(NativeDatabaseFlowFixture fixture, bool surreal, CancellationToken token)
    {
        await using var owner = new DocumentNativeTargetAcquisition();
        try
        {
            owner.client = await fixture.ClientAsync(token).ConfigureAwait(false);
            owner.target = surreal
                ? new SurrealDbTarget(owner.client, Guid.NewGuid().ToString(), fixture.Image, NativeDatabaseFlowFixture.ExecutionOptions)
                : new HelixDbTarget(owner.client, Guid.NewGuid().ToString(), fixture.Image, NativeDatabaseFlowFixture.ExecutionOptions);
            owner.client = null;
            var result = owner.target;
            owner.target = null;
            return result;
        }
        catch (Exception failure) { owner.primary = failure; throw; }
    }
    public async ValueTask DisposeAsync()
    {
        var targetFailure = await OpenLoopFailure.ObserveAsync(DisposeTargetAsync()).ConfigureAwait(false);
        var clientFailure = await OpenLoopFailure.ObserveAsync(DisposeClientAsync()).ConfigureAwait(false);
        var cleanup = new List<Exception>();
        if (targetFailure is not null)
        {
            cleanup.Add(targetFailure);
        }

        if (clientFailure is not null)
        {
            cleanup.Add(clientFailure);
        }

        var combined = OpenLoopFailure.Combine(primary, [.. cleanup]);
        if (cleanup.Count != EmptyCount && combined is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(combined).Throw();
        }
    }
    private const int EmptyCount = 0;
    private async Task DisposeTargetAsync()
    {
        if (target is not null)
        {
            await target.DisposeAsync().ConfigureAwait(false);
        }
    }
    private async Task DisposeClientAsync() { await Task.CompletedTask.ConfigureAwait(false); client?.Dispose(); }
}
