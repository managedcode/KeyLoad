using System.Runtime.ExceptionServices;
using KeyLoad.Orleans;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal readonly record struct RequestCqrsFatalCase(Exception Fatal, Exception Thrown);

internal static class RequestCqrsFatalSettlementProbe
{
    private const string SentinelMessage = "native-fatal-settlement-sentinel";
    private const string OuterAggregateMessage = "outer native aggregate";
    private const string InnerAggregateMessage = "inner native aggregate";
    private const string NestedCanary = "nested-fatal-settlement-canary";

    internal static IEnumerable<RequestCqrsFatalCase> FatalCases()
    {
        Type[] fatalTypes = [typeof(OutOfMemoryException), typeof(StackOverflowException), typeof(AccessViolationException)];
        foreach (var fatalType in fatalTypes)
        {
            var fatal = CreateFatal(fatalType);
            yield return new(fatal, fatal);
            var nested = new AggregateException(OuterAggregateMessage,
                new IOException(NestedCanary), new AggregateException(InnerAggregateMessage, fatal));
            yield return new(fatal, nested);
        }
    }

    internal static Exception DifferentFatal(Exception primary)
    {
        var type = primary switch
        {
            OutOfMemoryException => typeof(StackOverflowException),
            StackOverflowException => typeof(AccessViolationException),
            _ => typeof(OutOfMemoryException)
        };
        return CreateFatal(type);
    }

    private static Exception CreateFatal(Type type)
        => (Exception)Activator.CreateInstance(type, SentinelMessage)!;

    internal static Action Activation(RequestCqrsFatalObservation observation, Exception? failure = null)
        => () =>
        {
            observation.MarkActivationSettled();
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        };

    internal static async Task<Exception> CaptureExpectedAsync(Func<Task> operation, Exception expected)
    {
        try
        {
            await operation();
        }
        catch (Exception error) when (ReferenceEquals(error, expected))
        {
            return error;
        }

        throw new InvalidOperationException(SentinelMessage);
    }

    internal static async ValueTask<Result<GrainOperationReply>> ThrowAfterStartedAsync(
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, Guid requestId,
        Exception failure, RequestCqrsFatalObservation observation)
    {
        try
        {
            await writer.StartedAsync(new GrainRequestProgress(requestId));
            throw failure;
        }
        finally
        {
            observation.MarkProducerSettled();
        }
    }

    internal static async ValueTask<Result<GrainOperationReply>> WaitThenThrowAsync(
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, Guid requestId,
        Exception failure, RequestCqrsFatalObservation observation)
    {
        try
        {
            await writer.StartedAsync(new GrainRequestProgress(requestId));
            await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, writer.CancellationToken);
            return Result<GrainOperationReply>.Succeed(new GrainOperationReply { Payload = new byte[] { 1 } });
        }
        finally
        {
            observation.MarkProducerSettled();
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    internal static async ValueTask<Result<GrainOperationReply>> CompleteAsync(
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, Guid requestId,
        RequestCqrsFatalObservation observation)
    {
        try
        {
            await writer.StartedAsync(new GrainRequestProgress(requestId));
            return Result<GrainOperationReply>.Succeed(new GrainOperationReply { Payload = new byte[] { 1 } });
        }
        finally
        {
            observation.MarkProducerSettled();
        }
    }
}

internal sealed class RequestCqrsFatalObservation
{
    private readonly List<CqrsStreamChunkKind> observedKinds = [];
    private readonly List<Guid?> observedRequestIds = [];
    private int sequence;
    private int producerSettlementCount;
    private int activationSettlementCount;
    private int producerSettlementOrder;
    private int activationSettlementOrder;

    internal int ProducerSettlementCount => Volatile.Read(ref producerSettlementCount);
    internal int ActivationSettlementCount => Volatile.Read(ref activationSettlementCount);
    internal int ProducerSettlementOrder => Volatile.Read(ref producerSettlementOrder);
    internal int ActivationSettlementOrder => Volatile.Read(ref activationSettlementOrder);
    internal int ObservedChunkCount => observedKinds.Count;

    internal CqrsStreamChunkKind ObservedChunkKind(int index) => observedKinds[index];
    internal Guid? ObservedRequestId(int index) => observedRequestIds[index];

    internal void MarkChunkObserved(CqrsStreamChunkKind kind, Guid? requestId)
    {
        observedKinds.Add(kind);
        observedRequestIds.Add(requestId);
    }

    internal void MarkProducerSettled()
    {
        Interlocked.Increment(ref producerSettlementCount);
        Interlocked.Exchange(ref producerSettlementOrder, Interlocked.Increment(ref sequence));
    }

    internal void MarkActivationSettled()
    {
        Interlocked.Increment(ref activationSettlementCount);
        Interlocked.Exchange(ref activationSettlementOrder, Interlocked.Increment(ref sequence));
    }
}

internal static class RequestCqrsFatalStreamDrain
{
    internal static async Task DrainFatalPullAsync(
        IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> stream,
        Guid requestId, RequestCqrsFatalObservation observation, CancellationToken cancellationToken)
    {
        await foreach (var chunk in stream.WithBatchSize(GrainRequestStreamProtocol.BatchSize)
                           .WithCancellation(cancellationToken))
        {
            observation.MarkChunkObserved(chunk.Kind, chunk.ProgressResult?.Value?.RequestId);
        }

        await Assert.That(observation.ObservedChunkCount).IsEqualTo(1);
        await Assert.That(observation.ObservedChunkKind(0)).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(observation.ObservedRequestId(0)).IsEqualTo(requestId);
    }

    internal static async Task CancelAndDisposeAfterStartedAsync(
        IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> stream,
        Guid requestId, CancellationTokenSource cancellation, RequestCqrsFatalObservation observation)
    {
        await foreach (var chunk in stream.WithBatchSize(GrainRequestStreamProtocol.BatchSize)
                           .WithCancellation(cancellation.Token))
        {
            observation.MarkChunkObserved(chunk.Kind, chunk.ProgressResult?.Value?.RequestId);
            await cancellation.CancelAsync();
            break;
        }

        await Assert.That(observation.ObservedRequestId(0)).IsEqualTo(requestId);
    }

    internal static async Task DrainCompletedAsync(
        IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> stream,
        Guid requestId, RequestCqrsFatalObservation observation, CancellationToken cancellationToken)
    {
        await foreach (var chunk in stream.WithBatchSize(GrainRequestStreamProtocol.BatchSize)
                           .WithCancellation(cancellationToken))
        {
            observation.MarkChunkObserved(chunk.Kind, chunk.ProgressResult?.Value?.RequestId);
        }

        await Assert.That(observation.ObservedChunkCount).IsEqualTo(2);
        await Assert.That(observation.ObservedChunkKind(0)).IsEqualTo(CqrsStreamChunkKind.Started);
        await Assert.That(observation.ObservedChunkKind(1)).IsEqualTo(CqrsStreamChunkKind.Completed);
        await Assert.That(observation.ObservedRequestId(0)).IsEqualTo(requestId);
    }

    internal static async Task DrainAsync(
        IAsyncEnumerable<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>> stream,
        Guid requestId, RequestCqrsFatalObservation observation, CancellationToken cancellationToken)
    {
        await foreach (var chunk in stream.WithBatchSize(GrainRequestStreamProtocol.BatchSize)
                           .WithCancellation(cancellationToken))
        {
            observation.MarkChunkObserved(chunk.Kind, chunk.ProgressResult?.Value?.RequestId);
        }

        await Assert.That(observation.ObservedRequestId(0)).IsEqualTo(requestId);
    }

}
