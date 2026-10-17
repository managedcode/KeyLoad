using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

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
        if (original is null)
        {
            return null;
        }
        try
        {
            original.Dispose();
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
