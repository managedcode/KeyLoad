using KeyLoad.Clustering;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class ConnectionProducerCleanupFlow
{
    private const int ExpectedFailures = 3;
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    internal static async Task RunAsync(RequestCqrsHostedRuntimeFixture fixture)
    {
        var requestId = Guid.NewGuid();
        var wrongStartedId = Guid.NewGuid();
        var disposalFailure = new IOException("execution-producer-cancellation-cleanup");
        var notificationFailure = new IOException("execution-awaited-notification-cleanup");
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var producerOrder = 0;
        var notificationOrder = 0;
        var order = 0;
        using var deadline = new CancellationTokenSource(Bound, TimeProvider.System);
        using var token = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
            TestContext.Current!.Execution.CancellationToken);
        async ValueTask<Result<GrainOperationReply>> ProduceAsync(ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer)
        {
            using var cancellation = writer.CancellationToken.Register(() => throw disposalFailure);
            try
            {
                await writer.StartedAsync(new GrainRequestProgress(wrongStartedId));
                await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, writer.CancellationToken);
                throw new InvalidOperationException("The execution producer must be cancelled by original disposal.");
            }
            finally { Volatile.Write(ref producerOrder, Interlocked.Increment(ref order)); }
        }
        async ValueTask NotifyAsync()
        {
            await Assert.That(Volatile.Read(ref producerOrder)).IsGreaterThan(0);
            notificationOrder = Interlocked.Increment(ref order);
            arrived.SetResult();
            await release.Task;
            throw notificationFailure;
        }
        var stream = CqrsStreamLifetime.RunWithAwaitedSettlement(
            execution => CqrsStream.Create<GrainRequestProgress, GrainOperationReply>(ProduceAsync, execution),
            fixture.Services.GetRequiredService<Serializer<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>>(),
            requestId, fixture.Clock, NotifyAsync, fixture.RoutingOptions, fixture.RequestWork,
            new CqrsStreamPurpose(), token.Token);
        var original = DrainFailureAsync(stream, token.Token);
        try
        {
            await arrived.Task.WaitAsync(token.Token);
            await ConnectionAwaitedSettlementAssertions.ChargedAsync(fixture.RequestWork, requestId);
            release.SetResult();
            var failure = await original;
            await Assert.That(failure is AggregateException).IsTrue();
            var leaves = ((AggregateException)failure).Flatten().InnerExceptions;
            await Assert.That(leaves.Count).IsEqualTo(ExpectedFailures);
            await Assert.That(leaves.Any(error => ReferenceEquals(error, disposalFailure))).IsTrue();
            await Assert.That(leaves.Any(error => ReferenceEquals(error, notificationFailure))).IsTrue();
            await Assert.That(leaves.OfType<KeyLoadException>().Single().Code).IsEqualTo(ErrorCode.OwnershipLost);
            await Assert.That(notificationOrder).IsGreaterThan(producerOrder);
            await ConnectionAwaitedSettlementAssertions.ReleasedAsync(fixture.RequestWork, requestId, token.Token);
        }
        finally { release.TrySetResult(); await original; }
    }

    private static async Task<Exception> DrainFailureAsync(
        IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> stream, CancellationToken token)
    {
        try
        {
            await foreach (var unused in stream.WithCancellation(token))
            {
            }
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error)) { return error; }
        throw new InvalidOperationException("The genuine execution primary and cleanup failures must escape.");
    }
}
