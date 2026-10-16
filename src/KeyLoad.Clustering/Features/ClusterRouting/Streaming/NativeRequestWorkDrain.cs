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

    internal static async Task DisposeAsync(Task start, Func<Task> drain, Action dispose)
    {
        await start.ConfigureAwait(false);
        var drainFailure = await NativeRequestWorkSettlement.JoinAsync(drain()).ConfigureAwait(false);
        Exception? disposeFailure = null;
        try
        {
            dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            disposeFailure = error;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            disposeFailure = error;
        }
        NativeRequestWorkSettlement.Rethrow(NativeRequestWorkSettlement.Preserve(drainFailure, disposeFailure));
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
