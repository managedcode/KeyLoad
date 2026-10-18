using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Cancels and joins every originally admitted request frame during silo shutdown.</summary>
internal static class NativeRequestWorkDrain
{
    internal static async Task<Exception?> JoinAsync(Task start, CancellationTokenSource shutdown,
        Task<NativeRequestWorkOriginalSettlement>[] frames)
    {
        await start.ConfigureAwait(false);
        var failure = await NativeRequestWorkSettlement.CancelAsync(shutdown).ConfigureAwait(false);
        foreach (var frame in frames)
        {
            failure = NativeRequestWorkSettlement.Preserve(failure, await ObserveFrameAsync(frame).ConfigureAwait(false));
        }
        return failure;
    }

    internal static async Task JoinAndPublishAsync(Task start, CancellationTokenSource shutdown,
        Task<NativeRequestWorkOriginalSettlement>[] frames, Action joined)
    {
        var failure = await JoinAsync(start, shutdown, frames).ConfigureAwait(false);
        joined();
        NativeRequestWorkSettlement.Rethrow(failure);
    }

    internal static Task Start(Lock gate, ref Task? original, ref bool admissionClosed,
        CancellationTokenSource shutdown, NativeRequestWorkAdmissions admissions, NativeRequestWorkOwner owner)
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

    private static async Task<Exception?> ObserveFrameAsync(Task<NativeRequestWorkOriginalSettlement> original)
    {
        try
        {
            var outcome = await original.ConfigureAwait(false);
            return NativeRequestWorkSettlement.Preserve(CqrsRuntimeFailures.FindFatal(outcome.OriginalFailure), outcome.CleanupOrJoinFailure);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
    }

}
