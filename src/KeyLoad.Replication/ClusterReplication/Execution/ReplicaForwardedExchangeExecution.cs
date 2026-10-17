using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Replication;

/// <summary>Signs and publishes inside the original incoming frame before joining its complete cleanup.</summary>
internal static class ReplicaForwardedExchangeExecution
{
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
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null)
        {
            original = error;
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            original = error;
        }
        var publication = ReplicaForwardedReplyPublication.Execute(work, frame, result, original,
            createReply, measureReply, completed);
        var settled = await CompleteAsync(work, publication.Failure).ConfigureAwait(false);
        if (publication.Published && ReferenceEquals(settled, original))
        {
            ReplicaForwardReplyEncoding.Clear(result);
            return publication.Reply!;
        }
        ReplicaForwardReplyEncoding.Clear(publication.Encoded);
        ReplicaForwardReplyEncoding.Clear(result);
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(settled
            ?? Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidPeer)).Throw();
        throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidPeer);
    }

    private static async Task<Exception?> CompleteAsync(IReplicaForwardedCommandWork work, Exception? original)
    {
        try
        {
            return await work.CompleteAsync(original).ConfigureAwait(false);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null)
        {
            return ReplicaForwardedCommandCompletion.Preserve(original, error);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null)
        {
            return ReplicaForwardedCommandCompletion.Preserve(original, error);
        }
    }
}
