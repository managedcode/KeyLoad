using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class NativeCapabilityWorkSettlement
{
    private const int NoFailures = 0;
    private const int OneFailure = 1;
    private const int FirstFailure = 0;

    internal static async Task SettleAsync(Exception? primary, NativeCapabilityWorkLifetime? lifetime,
        Action? telemetry, Action? activation)
    {
        var failures = default(NativeCapabilityCleanupFailures);
        Capture(telemetry, ref failures);
        Capture(activation, ref failures);
        var preparation = await PrepareAsync(lifetime).ConfigureAwait(true);
        var lease = preparation is null ? lifetime?.CaptureAsyncRelease(ref failures) : null;
        var retainedCleanup = preparation is null ? lifetime?.ReleaseOwnedRetention() : null;
        var cleanup = NativeRequestWorkSettlement.Preserve(preparation,
            NativeRequestWorkSettlement.Preserve(Cleanup(failures), retainedCleanup));
        var terminal = cleanup;
        if (lease is not null)
        {
            try
            {
                var report = await lease.CompleteAsync(primary, cleanup).ConfigureAwait(true);
                terminal = report.CleanupOrJoinFailure;
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                terminal = NativeRequestWorkSettlement.Preserve(cleanup, error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                terminal = NativeRequestWorkSettlement.Preserve(cleanup, error);
            }
        }
        if (terminal is not null)
        {
            NativeRequestWorkSettlement.Rethrow(NativeRequestWorkSettlement.Preserve(primary, terminal));
        }
    }

    private static async Task<Exception?> PrepareAsync(NativeCapabilityWorkLifetime? lifetime)
    {
        try
        {
            if (lifetime is not null)
            {
                await lifetime.PrepareOwnedReleaseAsync().ConfigureAwait(true);
            }
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

    private static Exception? Cleanup(NativeCapabilityCleanupFailures failures)
    {
        if (failures.Count == NoFailures)
        {
            return null;
        }
        var errors = new List<Exception>();
        failures.AppendDistinct(null, errors);
        return errors.Count == OneFailure ? errors[FirstFailure] : new AggregateException(errors);
    }

    internal static void Capture(Action? operation, ref NativeCapabilityCleanupFailures failures)
    {
        if (operation is null)
        {
            return;
        }

        try
        {
            operation();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
    }

    internal static void ThrowCombined(Exception? primary, NativeCapabilityCleanupFailures cleanup)
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

/// <summary>Retains the four ordered cleanup failures possible for one capability settlement.</summary>
internal struct NativeCapabilityCleanupFailures
{
    private const string CleanupOverflowMessage = "Capability cleanup exceeded its fixed stages.";

    private Exception? first;
    private Exception? second;
    private Exception? third;
    private Exception? fourth;

    internal int Count
    {
        get; private set;
    }

    internal void Add(Exception error)
    {
        const int FirstCleanupFailureIndex = 0;
        const int SecondCleanupFailureIndex = 1;
        const int ThirdCleanupFailureIndex = 2;
        const int FourthCleanupFailureIndex = 3;

        switch (Count++)
        {
            case FirstCleanupFailureIndex:
                first = error;
                break;
            case SecondCleanupFailureIndex:
                second = error;
                break;
            case ThirdCleanupFailureIndex:
                third = error;
                break;
            case FourthCleanupFailureIndex:
                fourth = error;
                break;
            default:
                throw new InvalidOperationException(CleanupOverflowMessage);
        }
    }

    internal readonly Exception? FindFatal()
        => CqrsRuntimeFailures.FindFatal(first) ?? CqrsRuntimeFailures.FindFatal(second)
            ?? CqrsRuntimeFailures.FindFatal(third) ?? CqrsRuntimeFailures.FindFatal(fourth);

    internal readonly void AppendDistinct(Exception? primary, List<Exception> failures)
    {
        Append(first, primary, failures);
        Append(second, primary, failures);
        Append(third, primary, failures);
        Append(fourth, primary, failures);
    }

    private static void Append(Exception? error, Exception? primary, List<Exception> failures)
    {
        if (error is not null && !ReferenceEquals(error, primary))
        {
            failures.Add(error);
        }
    }
}

internal sealed class NativeCapabilityWorkRegistration(CancellationToken requestToken) : IDisposable
{
    private NativeRequestWorkLease? lease;
    private CancellationTokenSource? linkedCancellation;
    private bool settled;
    private IResourceLifetimeWorkBinding? originalBinding;
    private ResourceLifetimeWorkResultRetention? ownedRetention;
    internal CancellationToken Token => linkedCancellation?.Token ?? requestToken;
    internal NativeRequestWorkLease OriginalProducer => originalBinding is INativeRequestWorkBinding native
        ? native.OriginalProducer : throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
    internal ResourceLifetimeWorkResultRetention ResultRetention => originalBinding?.ResultRetention
        ?? lease?.ResultRetention ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);

    internal void Bind(ResourceLifetimeCoverageV1 coverage)
    {
        var original = originalBinding ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        var current = lease ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        original.Bind(coverage);
        current.Bind(coverage);
    }

    internal void Bind(ImmutableArray<ResourceLifetimeV1> lifetimes)
    {
        var original = originalBinding ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        var current = lease ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        original.Bind(lifetimes);
        current.Bind(lifetimes);
    }

    internal void Admit(NativeRequestWorkOwner owner, Guid requestId, NativeRequestWorkKind kind,
        IResourceLifetimeWorkBinding? binding = null, IOptions<DatabaseLimits>? ownedLimits = null)
    {
        lease = owner.Acquire(requestId, kind);
        originalBinding = binding;
        if (binding is not null)
        {
            lease.AttachResultRetention(binding.ResultRetention);
        }
        else if (ownedLimits is not null)
        {
            ownedRetention = new(ownedLimits);
            lease.AttachResultRetention(ownedRetention);
            originalBinding = lease;
        }
        linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(requestToken, owner.ShutdownToken,
            lease.Cancellation);
    }

    public void Dispose()
    {
        const int NoCleanupFailures = 0;
        var failures = default(NativeCapabilityCleanupFailures);
        Release(ref failures);
        if (failures.Count > NoCleanupFailures)
        {
            NativeCapabilityWorkSettlement.ThrowCombined(null, failures);
        }
    }

    internal void Release(ref NativeCapabilityCleanupFailures failures)
    {
        if (settled)
        {
            return;
        }

        settled = true;
        CaptureCancellation(ref failures);
        if (ReleaseOwnedRetention() is { } retentionFailure)
        {
            failures.Add(retentionFailure);
        }
        if (lease is not null)
        {
            try
            {
                lease.Dispose();
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            lease = null;
        }
    }

    internal NativeRequestWorkLease? ReleaseAsyncState(ref NativeCapabilityCleanupFailures failures)
    {
        if (settled)
        {
            return null;
        }
        settled = true;
        CaptureCancellation(ref failures);
        var original = lease;
        lease = null;
        originalBinding = null;
        return original;
    }

    internal Task PrepareOwnedReleaseAsync() => ownedRetention is not null && lease is { } original
        ? original.PrepareCompletionAsync() : Task.CompletedTask;

    internal Exception? ReleaseOwnedRetention()
    {
        var original = ownedRetention;
        ownedRetention = null;
        return original is null ? null : NativeRequestWorkSettlement.Observe(original.Dispose);
    }

    private void CaptureCancellation(ref NativeCapabilityCleanupFailures failures)
    {
        if (linkedCancellation is not null)
        {
            try
            {
                linkedCancellation.Dispose();
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            linkedCancellation = null;
        }
    }

}
