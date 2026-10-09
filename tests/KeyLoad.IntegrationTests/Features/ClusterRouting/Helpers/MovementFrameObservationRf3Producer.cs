using KeyLoad.Server;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>No earlier SDK refusal can substitute for actual native receiver prefix evidence.</summary>
internal static class MovementFrameObservationRf3Producer
{
    internal static async Task RequireAsync(MovementFrameObservationFixture fixture,
        Task<Result<PartitionMoveResult>> call, CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var observed = fixture.WaitFirstAsync(wait.Token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            _ = await Task.WhenAny(observed, call).ConfigureAwait(false);
            if (!observed.IsCompletedSuccessfully)
            {
                var early = await call.ConfigureAwait(false);
                var missing = new InvalidOperationException(MovementFrameObservationFixtureProtocol.Invalid);
                if (early.IsFailed && Enum.TryParse<ErrorCode>(early.Problem?.ErrorCode, out var code)
                    && Enum.IsDefined(code))
                { throw new AggregateException(Errors.Fail(code, early.Problem?.Detail ?? MovementFrameObservationFixtureProtocol.Invalid), missing); }
                throw missing;
            }
            _ = await observed.ConfigureAwait(false);
            _ = await fixture.WaitAllAsync(wait.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(wait.CancelAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => JoinAsync(observed, wait.Token), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task JoinAsync(Task observed, CancellationToken originalWait)
    {
        try
        { await observed.ConfigureAwait(false); }
        catch (OperationCanceledException) when (originalWait.IsCancellationRequested) { }
    }
}
