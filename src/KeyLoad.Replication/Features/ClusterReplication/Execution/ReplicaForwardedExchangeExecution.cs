using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Replication;

/// <summary>Signs and publishes inside the original incoming frame before joining its complete cleanup.</summary>
internal static class ReplicaForwardedExchangeExecution
{
    private const string ResultExceeded = "The forwarded command result byte budget is exceeded.";

    internal static async Task<T> ExecuteAsync<T>(IReplicaForwardedCommandWork work,
        ReplicaRequestDispatcher dispatcher, ReplicaForwardRequestV1 frame,
        Func<ReadOnlyMemory<byte>, Exception?, T> createReply, Func<T, long> measureReply, Action<T> completed)
    {
        ReadOnlyMemory<byte> result = default;
        Exception? original = null;
        try
        {
            ReplicaForwardReplyEncoding.Constrain(frame, work);
            result = await dispatcher.HandleForwardAsync(frame, work, work.Cancellation).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            original = error;
        }
        var failure = original;
        var published = false;
        T? reply = default;
        ReadOnlyMemory<byte> encoded = default;
        if (CqrsRuntimeFailures.FindFatal(original) is null)
        {
            try
            {
                var accounted = ReplicaForwardReplyEncoding.CanAccount(frame, work, original);
                if (original is null || accounted)
                {
                    encoded = ReplicaForwardReplyEncoding.Encode(frame, result,
                        accounted ? (KeyLoadException?)original : null, work);
                }
                reply = Publish(work, encoded, accounted ? null : original, createReply, measureReply, completed);
                published = true;
            }
            catch (Exception error)
            {
                failure = ReplicaForwardedCommandCompletion.Preserve(original, error);
            }
        }
        var settled = await CompleteAsync(work, failure).ConfigureAwait(false);
        if (published && ReferenceEquals(settled, original))
        {
            ReplicaForwardReplyEncoding.Clear(result);
            return reply!;
        }
        ReplicaForwardReplyEncoding.Clear(encoded);
        ReplicaForwardReplyEncoding.Clear(result);
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(settled
            ?? Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidPeer)).Throw();
        throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidPeer);
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

    private static async Task<Exception?> CompleteAsync(IReplicaForwardedCommandWork work, Exception? original)
    {
        try
        {
            return await work.CompleteAsync(original).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            return ReplicaForwardedCommandCompletion.Preserve(original, error);
        }
    }
}
