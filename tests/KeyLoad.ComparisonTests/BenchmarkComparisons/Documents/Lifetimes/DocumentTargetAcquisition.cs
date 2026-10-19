using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Transfers the actual Aspire-discovered development HTTP client to its execution target.</summary>
internal sealed class DocumentTargetAcquisition : IAsyncDisposable
{
    private HttpClient? client;
    private IComparisonTarget? target;
    private Exception? primary;
    internal static async Task<IComparisonTarget> OpenAsync(DatabaseFlowFixture fixture, bool surreal, CancellationToken token)
    {
        await using var owner = new DocumentTargetAcquisition();
        try
        {
            owner.client = await fixture.ClientAsync(token).ConfigureAwait(false);
            owner.target = surreal
                ? new SurrealDbTarget(owner.client, Guid.NewGuid().ToString(), fixture.Image, DatabaseFlowFixture.ExecutionOptions)
                : new HelixDbTarget(owner.client, Guid.NewGuid().ToString(), fixture.Image, DatabaseFlowFixture.ExecutionOptions);
            owner.client = null;
            var result = owner.target;
            owner.target = null;
            return result;
        }
        catch (Exception failure) { owner.primary = failure; throw; }
    }
    public async ValueTask DisposeAsync()
    {
        var targetFailure = await RuntimeComparisonFailure.ObserveAsync(DisposeTargetAsync()).ConfigureAwait(false);
        var clientFailure = await RuntimeComparisonFailure.ObserveAsync(DisposeClientAsync()).ConfigureAwait(false);
        var cleanup = new List<Exception>();
        if (targetFailure is not null)
        {
            cleanup.Add(targetFailure);
        }

        if (clientFailure is not null)
        {
            cleanup.Add(clientFailure);
        }

        var combined = RuntimeComparisonFailure.Combine(primary, [.. cleanup]);
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
    private async Task DisposeClientAsync()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        client?.Dispose();
    }
}
