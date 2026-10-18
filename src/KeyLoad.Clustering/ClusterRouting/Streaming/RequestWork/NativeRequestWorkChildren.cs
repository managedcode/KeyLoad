using System.Collections.Immutable;

namespace KeyLoad.Clustering;

/// <summary>Handles verified child admission within the existing owner's gate and bounded frame table.</summary>
internal sealed class NativeRequestWorkChildren(Lock gate, NativeRequestWorkOwner owner,
    NativeResourceWorkPhysicalBinding physical, NativeRequestWorkAdmissions admissions,
    NativeResourceWorkRetentions retentions, Func<bool> isAdmissionClosed)
{
    internal NativeRequestWorkChildReservation Reserve(NativeRequestWorkLease parent,
        DecodedGrainRequest verified, PhysicalShardRecord expectedOwner, SiloStorageIdentity expectedRuntime)
    {
        var identity = NativeRequestWorkChildIdentity.Create(verified, expectedOwner, expectedRuntime);
        lock (gate)
        {
            RequireChildOwner(expectedOwner, expectedRuntime);
            retentions.RequireAdmissionSlot(admissions.Count);
            return admissions.ReserveChild(owner, parent, identity);
        }
    }

    internal NativeRequestWorkLease AcquireVerifiedProducer(DecodedGrainRequest verified,
        PhysicalShardRecord actualOwner, SiloStorageIdentity actualRuntime)
    {
        var identity = NativeRequestWorkChildIdentity.IsSelected(verified)
            ? NativeRequestWorkChildIdentity.Create(verified, actualOwner, actualRuntime) : null;
        lock (gate)
        {
            RequireChildOwner(actualOwner, actualRuntime);
            if (identity is not null)
            {
                return admissions.ConsumeChild(identity);
            }
            if (admissions.HasReservedChild(verified.Envelope.RequestId))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
            }
            retentions.RequireAdmissionSlot(admissions.Count);
            return admissions.Acquire(owner, verified.Envelope.RequestId, NativeRequestWorkKind.RequestProducer);
        }
    }

    internal NativeRequestWorkLease RequireOriginalParentBinding(NativeRequestWorkLease child)
    {
        lock (gate)
        {
            if (isAdmissionClosed())
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.AdmissionClosedMessage);
            }
            return admissions.RequireOriginalParentBinding(child);
        }
    }

    internal bool BeginCompletion(NativeRequestWorkChildReservation reservation)
    {
        lock (gate)
        {
            return reservation.CloseDispatch();
        }
    }

    internal void RecordCleanup(NativeRequestWorkLease parent, ImmutableArray<Exception> failures)
    {
        lock (gate)
        {
            admissions.RecordChildCleanup(parent, failures);
        }
    }

    private void RequireChildOwner(PhysicalShardRecord actualOwner, SiloStorageIdentity actualRuntime)
    {
        if (isAdmissionClosed())
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.AdmissionClosedMessage);
        }
        physical.RequireActual(actualOwner, actualRuntime);
    }

}
