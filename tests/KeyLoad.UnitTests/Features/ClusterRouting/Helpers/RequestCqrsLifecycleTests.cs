using KeyLoad.Orleans;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsLifecycleCases
{
    private static readonly TimeSpan SettlementBound = TimeSpan.FromSeconds(10);

    internal static async Task AcCrs003RealNativeProducerSettlesOnCancellation(RequestCqrsClusterFixture fixture)
    {
        var requestId = Guid.NewGuid();
        using var cancelled = new CancellationTokenSource();
        var observed = new RequestCqrsProducerObservation();
        var serializer = fixture.Cluster.ServiceProvider.GetRequiredService<
            Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>();
        var stream = NativeCqrsStreamLifetime.Run(token => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(
                writer => WaitForCancellationAsync(writer, requestId, observed), token), serializer, requestId, TimeProvider.System, observed.MarkActivationSettled, options: fixture.RoutingOptions, owner: null, cancellationToken: cancelled.Token);

        await using (var enumerator = stream.GetAsyncEnumerator(cancelled.Token))
        {
            await Assert.That(await enumerator.MoveNextAsync()).IsTrue();
            await Assert.That(enumerator.Current.Kind).IsEqualTo(CqrsStreamChunkKind.Started);
            await observed.Started.Task.WaitAsync(SettlementBound, TimeProvider.System);
            await cancelled.CancelAsync();
            Exception? moveFailure = null;
            try
            {
                _ = await enumerator.MoveNextAsync().AsTask().WaitAsync(SettlementBound, TimeProvider.System);
            }
            catch (OperationCanceledException error)
            {
                moveFailure = error;
            }

            await Assert.That(moveFailure).IsTypeOf<OperationCanceledException>();
            await Assert.That(((OperationCanceledException)moveFailure!).CancellationToken.IsCancellationRequested).IsTrue();
        }

        await observed.ProducerSettled.Task.WaitAsync(SettlementBound, TimeProvider.System);
        await observed.ActivationSettled.Task.WaitAsync(SettlementBound, TimeProvider.System);
        await Assert.That(observed.CancellationObserved).IsTrue();
        await Assert.That(observed.ProducerSettlementCount).IsEqualTo(1);
        await Assert.That(observed.ActivationSettlementCount).IsEqualTo(1);
    }

    private static async ValueTask<Result<GrainOperationReply>> WaitForCancellationAsync(
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, Guid requestId,
        RequestCqrsProducerObservation observed)
    {
        try
        {
            await writer.StartedAsync(new GrainRequestProgress(requestId));
            observed.MarkStarted();
            await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, writer.CancellationToken);
            return Result<GrainOperationReply>.Succeed(
                new GrainOperationReply { Payload = new byte[] { 1 } });
        }
        finally
        {
            observed.MarkProducerSettled(writer.CancellationToken.IsCancellationRequested);
        }
    }
}

internal sealed class RequestCqrsProducerObservation
{
    internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource ProducerSettled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource ActivationSettled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int ProducerSettlementCount { get; private set; }
    internal int ActivationSettlementCount { get; private set; }
    internal bool CancellationObserved { get; private set; }

    internal void MarkStarted() => Started.TrySetResult();

    internal void MarkProducerSettled(bool cancellationObserved)
    {
        ProducerSettlementCount++;
        CancellationObserved = cancellationObserved;
        ProducerSettled.TrySetResult();
    }

    internal void MarkActivationSettled()
    {
        ActivationSettlementCount++;
        ActivationSettled.TrySetResult();
    }
}
