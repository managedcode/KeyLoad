using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnCanonicalSource
{
    internal static NativeAnnManifest Manifest(AnnMaintenanceRequest request, AnnSeed seed,
        string leaf, PackedAnnOptions policy)
    {
        RequireRequest(request, seed);
        var cut = Source(seed);
        return new(NativeAnnProtocol.Version, leaf, request.Consumer, request.IndexGeneration,
            seed.Scope.PrincipalId, request.Collection, request.Field, request.Space, request.Placement,
            cut, policy, seed.Records.Length, [], null)
        {
            ReplayAfter = seed.ProjectionCheckpoint ?? seed.Cut.OutboxTail,
            ReplayThrough = seed.Cut.OutboxTail,
            ReplayCorpusSha256 = seed.CorpusSha256
        };
    }

    internal static AnnSourceCut Source(AnnSeed seed)
    {
        return new AnnSourceCut(seed.Cut.NodeId, seed.Cut.Incarnation, seed.Cut.Position,
            seed.Cut.AppliedPosition, seed.Cut.OutboxTail, seed.Cut.ReadGeneration, seed.Scope.SchemaVersion,
            seed.Scope.PolicyEpoch, seed.CorpusSha256, seed.Cut.StoreFormatVersion, seed.Cut.KeyCodecVersion,
            seed.DependencySha256 ?? throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.InvalidSource),
            seed.Cut.OutboxFirstAvailable);
    }

    internal static void RequireRequest(AnnMaintenanceRequest request, AnnSeed seed)
    {
        if (request.NodeId != seed.Cut.NodeId || request.Placement.Incarnation != seed.Cut.Incarnation
            || request.Consumer.Partition != seed.Scope.Partition || request.Collection != seed.Scope.Collection
            || request.Field != seed.Scope.Field || request.Space != seed.Scope.Space)
        { throw Errors.Fail(ErrorCode.OwnershipLost, NativeAnnProtocol.InvalidSource); }
    }

    internal static void RequireRestore(AnnMaintenanceRequest request, AnnSeed current, NativeAnnManifest manifest)
    {
        RequireRestoreIdentity(request, current, manifest);
        if (manifest.Source.DependencySha256 != current.DependencySha256)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory); }
        if (manifest.Source.SchemaVersion != current.Scope.SchemaVersion
            || manifest.Source.PolicyEpoch != current.Scope.PolicyEpoch)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, NativeAnnProtocol.InvalidSource); }
    }

    internal static void RequireRestoreIdentity(AnnMaintenanceRequest request, AnnSeed current, NativeAnnManifest manifest)
    {
        RequireRequest(request, current);
        if (manifest.Consumer != request.Consumer || manifest.IndexGeneration != request.IndexGeneration
            || manifest.PrincipalId != current.Scope.PrincipalId || manifest.Collection != request.Collection
            || manifest.Field != request.Field || manifest.Space != request.Space
            || manifest.Source.NodeId != current.Cut.NodeId || manifest.Source.Incarnation != current.Cut.Incarnation
            || manifest.Source.StoreFormatVersion != current.Cut.StoreFormatVersion
            || manifest.Source.KeyCodecVersion != current.Cut.KeyCodecVersion
            || manifest.Source.ThroughSequence > current.Cut.OutboxTail
            || manifest.Source.Position > current.Cut.Position || manifest.Source.AppliedPosition > current.Cut.AppliedPosition
            || !SamePlacement(manifest.Placement, request.Placement))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, NativeAnnProtocol.InvalidSource); }
    }

    internal static void RequireExactUpper(AnnSeed upper, AnnSeed actual)
    {
        if (upper.Scope.PrincipalId != actual.Scope.PrincipalId || upper.Scope.PolicyEpoch != actual.Scope.PolicyEpoch
            || upper.Scope.Partition != actual.Scope.Partition || upper.Scope.Collection != actual.Scope.Collection
            || upper.Scope.Field != actual.Scope.Field || upper.Scope.SchemaVersion != actual.Scope.SchemaVersion
            || upper.Scope.Space != actual.Scope.Space || upper.CorpusSha256 != actual.CorpusSha256
            || upper.Records.Length != actual.Records.Length)
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.InvalidSource); }
    }

    private static bool SamePlacement(PhysicalShardRecord left, PhysicalShardRecord right)
        => left.PhysicalShardId == right.PhysicalShardId && left.Incarnation == right.Incarnation
            && left.PlacementEpoch == right.PlacementEpoch
            && left.VoterIds.SequenceEqual(right.VoterIds, StringComparer.Ordinal);
}
