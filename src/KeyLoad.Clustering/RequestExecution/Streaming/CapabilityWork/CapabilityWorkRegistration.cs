using System.Collections.Immutable;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.StoragePartitions.Contracts;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal sealed class CapabilityWorkRegistration(CancellationToken requestToken) : IDisposable
{
    private RequestWorkLease? lease;
    private CancellationTokenSource? linkedCancellation;
    private bool settled;
    private IResourceLifetimeWorkBinding? originalBinding;
    private ResourceLifetimeWorkResultRetention? ownedRetention;
    internal CancellationToken Token => linkedCancellation?.Token ?? requestToken;
    internal RequestWorkLease OriginalProducer => originalBinding is IRequestWorkBinding execution
        ? execution.OriginalProducer : throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
    internal ResourceLifetimeWorkResultRetention ResultRetention => originalBinding?.ResultRetention
                                                                    ?? lease?.ResultRetention ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);

    internal void Bind(ResourceLifetimeCoverage coverage)
    {
        var original = originalBinding ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        var current = lease ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        original.Bind(coverage);
        current.Bind(coverage);
    }

    internal void Bind(ImmutableArray<ResourceLifetime> lifetimes)
    {
        var original = originalBinding ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        var current = lease ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        original.Bind(lifetimes);
        current.Bind(lifetimes);
    }

    internal void Admit(RequestWorkOwner owner, Guid requestId, RequestWorkKind kind,
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
        var failures = default(CapabilityCleanupFailures);
        Release(ref failures);
        if (failures.Count > NoCleanupFailures)
        {
            CapabilityWorkSettlement.ThrowCombined(null, failures);
        }
    }

    internal void Release(ref CapabilityCleanupFailures failures)
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
            catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            lease = null;
        }
    }

    internal RequestWorkLease? ReleaseAsyncState(ref CapabilityCleanupFailures failures)
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
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return error;
        }
    }

    private void CaptureCancellation(ref CapabilityCleanupFailures failures)
    {
        if (linkedCancellation is not null)
        {
            try
            {
                linkedCancellation.Dispose();
            }
            catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            linkedCancellation = null;
        }
    }

}
