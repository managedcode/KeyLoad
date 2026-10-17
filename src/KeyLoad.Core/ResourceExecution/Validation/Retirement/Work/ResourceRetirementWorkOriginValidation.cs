using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceRetirementWorkOriginValidation
{
    internal static ImmutableArray<ResourceRetirementWorkOriginV1> Capture(
        ImmutableArray<ResourceRetirementOwnerStatusV1> statuses, PhysicalOwnerDirectoryV1 directory, DatabaseLimits limits)
    {
        ResourceRetirementValidation.Bound(statuses, limits);
        if (statuses.IsDefaultOrEmpty || statuses.Length > limits.MaxResults || statuses.Length > limits.MaxScanRecords)
        {
            throw Invalid();
        }
        if (statuses.All(status => status is not null && status.VoterId is null && status.FullSiloAddress is null))
        {
            return ImmutableArray<ResourceRetirementWorkOriginV1>.Empty;
        }
        var origins = ImmutableArray.CreateBuilder<ResourceRetirementWorkOriginV1>(statuses.Length);
        foreach (var status in statuses)
        {
            ResourceRetirementPreparationValidation.Status(status, limits);
            if (status.VoterId is null || status.FullSiloAddress is null)
            {
                throw Invalid();
            }
            origins.Add(new(ResourceRetirementProtocol.Version, status.Reservation.Receiver.Owner,
                status.VoterId, status.FullSiloAddress, status.Cut.NodeId, status.Cut.StoreIncarnation, status.Cut.ReadGeneration));
        }
        var result = origins.MoveToImmutable();
        RequireSet(result, directory, limits);
        return result;
    }

    internal static void Optional(ResourceRetirementControlV1 control, DatabaseLimits limits)
    {
        if (!control.WorkOrigins.IsDefaultOrEmpty)
        {
            RequireSet(control.WorkOrigins, control.Directory, limits);
        }
    }

    internal static void RequireCurrent(ResourceRetirementControlV1 control, PhysicalShardRecord owner,
        string voterId, string fullSiloAddress, Guid nodeId, Guid storeIncarnation, long readGeneration, DatabaseLimits limits)
    {
        var origin = Find(control, owner, voterId, limits);
        if (origin.FullSiloAddress != fullSiloAddress || origin.StoreNodeId != nodeId
            || origin.StoreIncarnation != storeIncarnation || origin.ReadGeneration != readGeneration)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
    }

    internal static void RequireWitness(ResourceRetirementControlV1 control,
        ResourceRetirementWorkJoinWitnessV1 witness, DatabaseLimits limits)
    {
        var origin = Find(control, witness.ActualOwner, witness.VoterId, limits);
        if (origin.FullSiloAddress != witness.FullSiloAddress || origin.StoreNodeId != witness.StoreNodeId
            || origin.ReadGeneration != witness.ReadGeneration)
        {
            throw Invalid();
        }
    }

    private static ResourceRetirementWorkOriginV1 Find(ResourceRetirementControlV1 control,
        PhysicalShardRecord owner, string voter, DatabaseLimits limits)
    {
        RequireSet(control.WorkOrigins, control.Directory, limits);
        return control.WorkOrigins.SingleOrDefault(origin => origin.VoterId == voter
            && PhysicalOwnerEntryValidation.SameOwner(origin.PhysicalOwner, owner))
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
    }

    private static void RequireSet(ImmutableArray<ResourceRetirementWorkOriginV1> origins,
        PhysicalOwnerDirectoryV1 directory, DatabaseLimits limits)
    {
        if (origins.IsDefaultOrEmpty || origins.Length > limits.MaxResults || origins.Length > limits.MaxScanRecords
            || origins.Length != directory.Owners.Sum(member => member.Owner.VoterIds.Length))
        {
            throw Invalid();
        }
        ResourceRetirementValidation.Bound(origins, limits);
        var voters = new HashSet<(Guid, string)>();
        var nodes = new HashSet<Guid>();
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var origin in origins)
        {
            RequireOrigin(origin, directory);
            if (!voters.Add((origin.PhysicalOwner.PhysicalShardId, origin.VoterId))
                || !nodes.Add(origin.StoreNodeId) || !addresses.Add(origin.FullSiloAddress))
            {
                throw Invalid();
            }
        }
    }

    private static void RequireOrigin(ResourceRetirementWorkOriginV1 origin, PhysicalOwnerDirectoryV1 directory)
    {
        if (origin is null || origin.Version != ResourceRetirementProtocol.Version || origin.PhysicalOwner is null
            || string.IsNullOrWhiteSpace(origin.VoterId) || string.IsNullOrWhiteSpace(origin.FullSiloAddress)
            || origin.FullSiloAddress != origin.FullSiloAddress.Trim() || origin.StoreNodeId == Guid.Empty
            || origin.StoreIncarnation == Guid.Empty || origin.StoreIncarnation != origin.PhysicalOwner.Incarnation
            || origin.ReadGeneration < ResourceRetirementProtocol.Empty
            || !directory.Owners.Any(member => PhysicalOwnerEntryValidation.SameOwner(member.Owner, origin.PhysicalOwner)
                && member.Owner.VoterIds.Contains(origin.VoterId, StringComparer.Ordinal)))
        {
            throw Invalid();
        }
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
}
