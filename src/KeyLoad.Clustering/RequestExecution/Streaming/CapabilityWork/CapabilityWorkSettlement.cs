using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

internal static class CapabilityWorkSettlement
{
    private const int NoFailures = 0;
    private const int OneFailure = 1;
    private const int FirstFailure = 0;

    internal static async Task SettleAsync(Exception? primary, CapabilityWorkLifetime? lifetime,
        Action? telemetry, Action? activation)
    {
        var failures = default(CapabilityCleanupFailures);
        Capture(telemetry, ref failures);
        Capture(activation, ref failures);
        var preparation = (await PrepareAsync(lifetime).ConfigureAwait(true)).Value;
        var lease = preparation is null ? lifetime?.CaptureAsyncRelease(ref failures) : null;
        var retainedCleanup = preparation is null ? lifetime?.ReleaseOwnedRetention() : null;
        var cleanup = RequestWorkSettlement.Preserve(preparation,
            RequestWorkSettlement.Preserve((Cleanup(failures)).Value, retainedCleanup));
        var terminal = cleanup;
        if (lease is not null)
        {
            try
            {
                lease.PrepareDisposal(primary, cleanup);
                await lease.DisposeAsync().ConfigureAwait(true);
                var report = await lease.Settlement.ConfigureAwait(true);
                terminal = report.CleanupOrJoinFailure;
            }
            catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
            {
                terminal = RequestWorkSettlement.Preserve(cleanup, error);
            }
            catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
            {
                terminal = RequestWorkSettlement.Preserve(cleanup, error);
            }
        }
        if (terminal is not null)
        {
            RequestWorkSettlement.Rethrow(RequestWorkSettlement.Preserve(primary, terminal));
        }
    }

    private static async Task<global::ManagedCode.Communication.Result<Exception?>> PrepareAsync(CapabilityWorkLifetime? lifetime)
    {
        try
        {
            if (lifetime is not null)
            {
                await lifetime.PrepareOwnedReleaseAsync().ConfigureAwait(true);
            }
            return global::ManagedCode.Communication.Result<Exception?>.FailNotFound(nameof(PrepareAsync));
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return global::ManagedCode.Communication.Result<Exception?>.Succeed(((Exception?)(error)));
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return global::ManagedCode.Communication.Result<Exception?>.Succeed(((Exception?)(error)));
        }
    }

    private static global::ManagedCode.Communication.Result<Exception?> Cleanup(CapabilityCleanupFailures failures)
    {
        if (failures.Count == NoFailures)
        {
            return global::ManagedCode.Communication.Result<Exception?>.FailNotFound(nameof(Cleanup));
        }
        var errors = new List<Exception>();
        failures.AppendDistinct(null, errors);
        return errors.Count == OneFailure ? global::ManagedCode.Communication.Result<Exception?>.Succeed(((Exception?)(errors[FirstFailure]))) : global::ManagedCode.Communication.Result<Exception?>.Succeed((Exception?)(new AggregateException(errors)));
    }

    internal static void Capture(Action? operation, ref CapabilityCleanupFailures failures)
    {
        if (operation is null)
        {
            return;
        }

        try
        {
            operation();
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
    }

    internal static void ThrowCombined(Exception? primary, CapabilityCleanupFailures cleanup)
    {
        const int EmptyFailuresCount = 1;
        const int IndexEmptyCount = 0;

        var fatal = CqrsRuntimeFailures.FindFatal(primary) ?? cleanup.FindFatal();
        if (fatal is not null)
        {
            ExceptionDispatchInfo.Capture(fatal).Throw();
        }

        const int NoPrimaryFailure = 0;
        const int SinglePrimaryFailure = 1;
        var failures = new List<Exception>(cleanup.Count + (primary is null ? NoPrimaryFailure : SinglePrimaryFailure));
        if (primary is not null)
        {
            failures.Add(primary);
        }
        cleanup.AppendDistinct(primary, failures);
        if (failures.Count == EmptyFailuresCount)
        {
            ExceptionDispatchInfo.Capture(failures[IndexEmptyCount]).Throw();
        }
        throw new AggregateException(failures);
    }
}
