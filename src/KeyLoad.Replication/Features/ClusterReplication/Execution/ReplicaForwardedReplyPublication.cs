using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Replication;

/// <summary>Encodes accounted costs and publishes one reply within its original incoming work frame.</summary>
internal static class ReplicaForwardedReplyPublication
{
    private const string ResultExceeded = "The forwarded command result byte budget is exceeded.";

    internal static (T? Reply, ReadOnlyMemory<byte> Encoded, Exception? Failure, bool Published) Execute<T>(
        IReplicaForwardedCommandWork work, ReplicaForwardRequestV1 frame, ReadOnlyMemory<byte> result,
        Exception? original, Func<ReadOnlyMemory<byte>, Exception?, T> createReply,
        Func<T, long> measureReply, Action<T> completed)
    {
        if (CqrsRuntimeFailures.FindFatal(original) is not null)
        {
            return (default, default, original, false);
        }
        ReadOnlyMemory<byte> encoded = default;
        try
        {
            var accounted = ReplicaForwardReplyEncoding.CanAccount(frame, work, original);
            if (original is null || accounted)
            {
                encoded = ReplicaForwardReplyEncoding.Encode(frame, result,
                    accounted ? (KeyLoadException?)original : null, work);
            }
            var reply = Publish(work, encoded, accounted ? null : original, createReply, measureReply, completed);
            return (reply, encoded, original, true);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null)
        {
            return (default, encoded, ReplicaForwardedCommandCompletion.Preserve(original, error), false);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            return (default, encoded, ReplicaForwardedCommandCompletion.Preserve(original, error), false);
        }
    }

    private static T Publish<T>(IReplicaForwardedCommandWork work, ReadOnlyMemory<byte> result, Exception? error,
        Func<ReadOnlyMemory<byte>, Exception?, T> createReply, Func<T, long> measureReply, Action<T> completed)
    {
        work.Budget.Check();
        var reply = createReply(result, error);
        work.Budget.CheckResult(reply);
        if (measureReply(reply) > work.Budget.MaximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResultExceeded);
        }
        work.AdmitPublication();
        completed(reply);
        return reply;
    }

}
