using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PlacementCapacityEnrollment
{
    internal static void Require(IKeyValueView view, PhysicalShardRecord control,
        ImmutableArray<PhysicalShardRecord> observedOwners)
    {
        var directory = PhysicalOwnerDirectorySerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementCapacityProtocol.WrongCohort);
        if (!PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, control))
        { throw Errors.Fail(ErrorCode.Corruption, PhysicalOwnerDirectoryProtocol.ControlMismatch); }
        var selected = observedOwners.Select(owner => owner.PhysicalShardId).Distinct().ToArray();
        if (selected.Length != directory.Owners.Length
            || directory.Owners.Any(entry => !selected.Contains(entry.Owner.PhysicalShardId)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementCapacityProtocol.WrongCohort); }
        foreach (var owner in observedOwners)
        {
            var enrolled = directory.Owners.SingleOrDefault(entry =>
                entry.Owner.PhysicalShardId == owner.PhysicalShardId);
            if (enrolled is null || !PhysicalOwnerEntryValidation.SameOwner(enrolled.Owner, owner))
            { throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementCapacityProtocol.WrongCohort); }
        }
    }
}
