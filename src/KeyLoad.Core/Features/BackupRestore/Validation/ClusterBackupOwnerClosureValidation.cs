using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

/// <summary>Validates complete actual owner closure without claiming a simultaneous cross-partition cut.</summary>
public static class ClusterBackupOwnerClosureValidation
{
    private const int NoOwners = 0;
    private const long NoPosition = 0;
    private const string Incomplete = "The cluster backup owner vector is incomplete or inconsistent.";

    /// <summary>Requires one genuine captured owner for every actual registered or assigned owner tuple.</summary>
    /// <param name="captureId">Common capture operation identity.</param>
    /// <param name="cuts">Actual verified native owner metadata, never declarations substituted for archives.</param>
    public static void Require(Guid captureId, ImmutableArray<ClusterBackupOwnerCut> cuts)
    {
        if (captureId == Guid.Empty || cuts.IsDefaultOrEmpty || cuts.Length > PhysicalOwnerDirectoryProtocol.MaximumOwners)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Incomplete); }
        var actual = new Dictionary<Guid, PhysicalShardRecord>(cuts.Length);
        var required = new Dictionary<Guid, PhysicalShardRecord>();
        foreach (var cut in cuts)
        {
            if (cut is null || cut.Version != ClusterBackupOwnerCut.CurrentVersion || cut.CaptureId != captureId
                || cut.Owner is null || cut.Owner.VoterIds.IsDefault || cut.Catalog is null
                || cut.StorePosition <= NoPosition || cut.AppliedIndex <= NoPosition || cut.Partitions.IsDefault)
            { throw Errors.Fail(ErrorCode.Corruption, Incomplete); }
            PhysicalShardCatalogValidation.ValidateCatalog(cut.Catalog);
            if (!PhysicalOwnerEntryValidation.SameOwner(cut.Owner, cut.Catalog.DefaultShard)
                || !actual.TryAdd(cut.Owner.PhysicalShardId, cut.Owner))
            { throw Errors.Fail(ErrorCode.Corruption, Incomplete); }
            Add(required, cut.Owner);
            if (cut.RegisteredOwners is not null)
            {
                PhysicalOwnerDirectoryValidation.Validate(cut.RegisteredOwners);
                foreach (var owner in cut.RegisteredOwners.Owners)
                { Add(required, owner.Owner); }
            }
            foreach (var partition in cut.Partitions)
            {
                if (partition is null || partition.Version != ClusterBackupOwnerCut.CurrentVersion)
                { throw Errors.Fail(ErrorCode.Corruption, Incomplete); }
                Add(required, ResolveAssignedGroup(cut, partition.Placement));
            }
        }
        ClusterBackupPartitionVectorValidation.Require(cuts);
        if (required.Count == NoOwners || required.Count != actual.Count
            || required.Any(owner => !actual.TryGetValue(owner.Key, out var found)
                || !PhysicalOwnerEntryValidation.SameOwner(owner.Value, found)))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Incomplete); }
    }

    private static PhysicalShardRecord ResolveAssignedGroup(ClusterBackupOwnerCut cut,
        AtomicPartitionPlacementResolution? placement)
    {
        if (placement is null || placement.VoterIds.IsDefault)
        { throw Errors.Fail(ErrorCode.Corruption, Incomplete); }
        var owner = placement.PhysicalShardId == cut.Owner.PhysicalShardId ? cut.Owner
            : cut.RegisteredOwners?.Owners.SingleOrDefault(entry =>
                entry.Owner.PhysicalShardId == placement.PhysicalShardId)?.Owner;
        if (owner is null || placement.Incarnation != owner.Incarnation
            || placement.PlacementEpoch < owner.PlacementEpoch
            || !placement.VoterIds.SequenceEqual(owner.VoterIds, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.Corruption, Incomplete); }
        return owner;
    }

    private static void Add(Dictionary<Guid, PhysicalShardRecord> owners, PhysicalShardRecord owner)
    {
        if (owners.TryGetValue(owner.PhysicalShardId, out var original))
        {
            if (!PhysicalOwnerEntryValidation.SameOwner(original, owner))
            { throw Errors.Fail(ErrorCode.Corruption, Incomplete); }
            return;
        }
        if (owners.Count >= PhysicalOwnerDirectoryProtocol.MaximumOwners)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, Incomplete); }
        owners.Add(owner.PhysicalShardId, owner);
    }
}
