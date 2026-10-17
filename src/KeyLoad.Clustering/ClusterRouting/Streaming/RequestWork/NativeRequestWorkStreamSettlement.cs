using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Settles rejected admission or an admitted native producer before releasing its frame.</summary>
internal static class NativeRequestWorkStreamSettlement
{
    internal static async ValueTask<NativeRequestWorkLease?> AcquireOrSettleAsync(
        NativeRequestWorkOwner? owner, Guid requestId, Action settled)
        => await AcquireOrSettleAwaitedAsync(owner, requestId, () => { settled(); return ValueTask.CompletedTask; }).ConfigureAwait(true);

    internal static async ValueTask<NativeRequestWorkLease?> AcquireOrSettleAwaitedAsync(
        NativeRequestWorkOwner? owner, Guid requestId, Func<ValueTask> settled)
    {
        var (lease, error) = TryAcquire(owner, requestId);
        if (error is null)
        {
            return lease;
        }

        var failure = await SettleAndReleaseAwaitedAsync<CqrsStreamChunk<GrainRequestProgress, GrainOperationReply>>(
            null, settled, error, null).ConfigureAwait(true);
        ExceptionDispatchInfo.Capture(failure ?? error).Throw();
        return null;
    }

    private static (NativeRequestWorkLease? Lease, Exception? Error) TryAcquire(
        NativeRequestWorkOwner? owner, Guid requestId)
    {
        if (owner is null)
        {
            return (null, null);
        }

        try
        {
            return (owner.Acquire(requestId, NativeRequestWorkKind.RequestProducer), null);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return (null, error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            return (null, error);
        }
    }

    internal static async ValueTask<Exception?> SettleAndReleaseAsync<T>(IAsyncEnumerator<T>? enumerator,
        Action settled, Exception? primary, NativeRequestWorkLease? lease)
        => await SettleAndReleaseAwaitedAsync(enumerator, () => { settled(); return ValueTask.CompletedTask; }, primary, lease).ConfigureAwait(true);

    internal static async ValueTask<Exception?> SettleAndReleaseAwaitedAsync<T>(IAsyncEnumerator<T>? enumerator,
        Func<ValueTask> settled, Exception? primary, NativeRequestWorkLease? lease)
    {
        Exception? failure = null;
        Exception? settlementError = null;
        try
        {
            failure = await NativeCqrsStreamSettlement.SettleAwaitedAsync(enumerator, settled, primary).ConfigureAwait(true);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            settlementError = error;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            settlementError = error;
        }

        var releaseFailure = Release(lease);
        if (settlementError is not null)
        {
            failure = NativeRequestWorkSettlement.Preserve(primary, settlementError);
        }

        return releaseFailure is null ? failure
            : NativeRequestWorkSettlement.Preserve(failure ?? primary, releaseFailure);
    }

    private static Exception? Release(NativeRequestWorkLease? lease)
    {
        if (lease is null)
        {
            return null;
        }

        try
        {
            lease.Dispose();
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
