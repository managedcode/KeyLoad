using System.Runtime.ExceptionServices;
using KeyLoad.Clustering;
using ManagedCode.Communication;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RequestCqrsFatalSettlementProbe
{
    private const string SentinelMessage = "execution-fatal-settlement-sentinel";
    private const string OuterAggregateMessage = "outer execution aggregate";
    private const string InnerAggregateMessage = "inner execution aggregate";
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
