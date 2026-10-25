using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.StoragePartitions.Contracts;

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
        IResourceLifetimeWorkBinding? binding = null, bool ownRetention = false)
    {
        lease = owner.Acquire(requestId, kind);
        originalBinding = binding;
        if (binding is not null)
        {
            lease.AttachResultRetention(binding.ResultRetention);
        }
        else if (ownRetention)
        {
            ownedRetention = new();
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
        if ((ReleaseOwnedRetention()).Value is { } retentionFailure)
        {
            failures.Add(retentionFailure);
        }
        ReleaseLease(ref failures);
    }

    private void ReleaseLease(ref CapabilityCleanupFailures failures)
    {
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

    internal bool TryReleaseAsyncState(ref CapabilityCleanupFailures failures,
        [NotNullWhen(true)] out RequestWorkLease? original)
    {
        original = null;
        if (settled)
        {
            return false;
        }
        settled = true;
        CaptureCancellation(ref failures);
        original = lease;
        lease = null;
        originalBinding = null;
        return original is not null;
    }

    internal Task PrepareOwnedReleaseAsync() => ownedRetention is not null && lease is { } original
        ? original.PrepareCompletionAsync() : Task.CompletedTask;

    internal (bool HasValue, Exception? Value) ReleaseOwnedRetention()
    {
        var original = ownedRetention;
        ownedRetention = null;
        if (original is null)
        {
            return (false, default(Exception?));
        }
        try
        {
            original.Dispose();
            return (false, default(Exception?));
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            return (true, ((Exception?)(error)));
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            return (true, ((Exception?)(error)));
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
