using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

/// <summary>Checks complete explicit configuration, separately from native operator authentication and publication.</summary>
public static class ClusterRestoreMappingValidation
{
    private const int RequiredOwnerCount = 1;
    private const int RequiredVoters = PhysicalOwnerDirectoryProtocol.VoterCount;
    private const string Invalid = "The restore configuration must map every original owner to a distinct new RF3 identity.";

    /// <summary>Enforces the existing native owner bound before offline filesystem work.</summary>
    /// <param name="count">Actual configured archive-owner count.</param>
    public static void RequireSourceCount(int count)
    {
        if (count < RequiredOwnerCount || count > PhysicalOwnerDirectoryProtocol.MaximumOwners)
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
    }

    /// <summary>Requires one bounded, fresh, complete target tuple per verified original owner.</summary>
    /// <param name="captureId">Actual shared original capture identity.</param>
    /// <param name="cuts">Complete verified source cut vector.</param>
    /// <param name="mappings">Separately configured exact target vector; no role or key is inferred.</param>
    public static void Require(Guid captureId, ImmutableArray<ClusterBackupOwnerCut> cuts,
        ImmutableArray<ClusterRestoreOwnerMapping> mappings)
    {
        ClusterBackupOwnerClosureValidation.Require(captureId, cuts);
        if (mappings.IsDefault || mappings.Length != cuts.Length)
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        var sources = cuts.ToDictionary(cut => cut.Owner.PhysicalShardId);
        var sourceIncarnations = cuts.Select(cut => cut.Owner.Incarnation).ToHashSet();
        var mappedSources = new HashSet<Guid>();
        var targetIds = new HashSet<Guid>();
        var targetIncarnations = new HashSet<Guid>();
        var voters = new HashSet<string>(StringComparer.Ordinal);
        var endpoints = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mapping in mappings)
        {
            RequireSource(mapping, sources, mappedSources);
            RequireTarget(mapping.Target, sources, sourceIncarnations, targetIds, targetIncarnations);
            RequireMembers(mapping, voters, endpoints);
        }
    }

    private static void RequireSource(ClusterRestoreOwnerMapping mapping,
        Dictionary<Guid, ClusterBackupOwnerCut> sources, HashSet<Guid> seen)
    {
        if (mapping is null || mapping.Version != ClusterRestoreOwnerMapping.CurrentVersion
            || mapping.Source is null || mapping.Source.VoterIds.IsDefault
            || !seen.Add(mapping.Source.PhysicalShardId)
            || !sources.TryGetValue(mapping.Source.PhysicalShardId, out var source)
            || !PhysicalOwnerEntryValidation.SameOwner(mapping.Source, source.Owner))
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
    }

    private static void RequireTarget(PhysicalShardRecord target,
        Dictionary<Guid, ClusterBackupOwnerCut> sources, HashSet<Guid> originalIncarnations,
        HashSet<Guid> targetIds, HashSet<Guid> incarnations)
    {
        if (target is null || target.PhysicalShardId == Guid.Empty || target.Incarnation == Guid.Empty
            || target.VoterIds.IsDefault || target.VoterIds.Length != RequiredVoters
            || target.PlacementEpoch != PhysicalShardCatalogProtocol.InitialPlacementEpoch
            || sources.ContainsKey(target.PhysicalShardId) || originalIncarnations.Contains(target.Incarnation)
            || !targetIds.Add(target.PhysicalShardId) || !incarnations.Add(target.Incarnation))
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
    }

    private static void RequireMembers(ClusterRestoreOwnerMapping mapping, HashSet<string> voters,
        HashSet<string> endpoints)
    {
        if (!PhysicalOwnerEntryValidation.Valid(new RegisteredPhysicalOwnerV1(mapping.Target, mapping.Endpoints)))
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        foreach (var voter in mapping.Target.VoterIds)
        {
            if (!voters.Add(voter))
            { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        }
        foreach (var endpoint in mapping.Endpoints)
        {
            if (!endpoints.Add(endpoint))
            { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        }
    }
}
