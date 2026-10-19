using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Cancels and joins every originally admitted request frame during silo shutdown.</summary>
internal static class RequestWorkDrain
{
    internal static async Task<Exception?> JoinAsync(Task start, CancellationTokenSource shutdown,
        Task<RequestWorkOriginalSettlement>[] frames)
    {
        await start;
        var failure = await RequestWorkSettlement.CancelAsync(shutdown);
        foreach (var frame in frames)
        {
            failure = RequestWorkSettlement.Preserve(failure, await ObserveFrameAsync(frame));
        }
        return failure;
    }

    internal static async Task JoinAndPublishAsync(Task start, CancellationTokenSource shutdown,
        Task<RequestWorkOriginalSettlement>[] frames, Action joined)
    {
        var failure = await JoinAsync(start, shutdown, frames);
        joined();
        RequestWorkSettlement.Rethrow(failure);
    }

    internal static Task Start(Lock gate, ref Task? original, ref bool admissionClosed,
        CancellationTokenSource shutdown, RequestWorkAdmissions admissions, RequestWorkOwner owner)
    {
        TaskCompletionSource start;
        Task task;
        lock (gate)
        {
            if (original is not null)
            {
                return original;
            }
            admissionClosed = true;
            var frames = admissions.Settlements;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            task = JoinAndPublishAsync(start.Task, shutdown, frames, owner.MarkJoined);
            original = task;
        }
        start.TrySetResult();
        return task;
    }

    private static async Task<Exception?> ObserveFrameAsync(Task<RequestWorkOriginalSettlement> original)
    {
        try
        {
            var outcome = await original;
            return RequestWorkSettlement.Preserve(CqrsRuntimeFailures.FindFatal(outcome.OriginalFailure), outcome.CleanupOrJoinFailure);
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
    }

}
