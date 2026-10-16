using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Drives every captured original retirement and frame when the existing global owner shuts down.</summary>
internal static class NativeRequestWorkDrain
{
    internal static async Task<Exception?> JoinAsync(Task start, CancellationTokenSource shutdown,
        Task<NativeRequestWorkOriginalSettlement>[] frames, NativeResourceWorkClosure[] retirements)
    {
        await start.ConfigureAwait(false);
        var closures = retirements.Select(ObserveRetirementAsync).ToArray();
        var failure = await NativeRequestWorkSettlement.CancelAsync(shutdown).ConfigureAwait(false);
        foreach (var frame in frames)
        {
            failure = NativeRequestWorkSettlement.Preserve(failure, await ObserveFrameAsync(frame).ConfigureAwait(false));
        }
        foreach (var closure in closures)
        {
            failure = NativeRequestWorkSettlement.Preserve(failure, await closure.ConfigureAwait(false));
        }
        return failure;
    }

    internal static async Task JoinAndPublishAsync(Task start, CancellationTokenSource shutdown,
        Task<NativeRequestWorkOriginalSettlement>[] frames, NativeResourceWorkClosure[] retirements, Action joined)
    {
        var failure = await JoinAsync(start, shutdown, frames, retirements).ConfigureAwait(false);
        joined();
        NativeRequestWorkSettlement.Rethrow(failure);
    }

    internal static Task Start(Lock gate, ref Task? original, ref bool admissionClosed,
        CancellationTokenSource shutdown, NativeRequestWorkAdmissions admissions, NativeResourceWorkRetentions retentions, NativeRequestWorkOwner owner)
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
            var retirements = retentions.Outstanding;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            task = JoinAndPublishAsync(start.Task, shutdown, frames, retirements, owner.MarkJoined);
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

    private static async Task<Exception?> ObserveRetirementAsync(NativeResourceWorkClosure original)
    {
        try
        {
            await original.CancelAndJoinAsync().ConfigureAwait(false);
            return null;
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
