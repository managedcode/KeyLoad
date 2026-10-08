using KeyLoad.Core.Features.Search;
using KeyLoad.Orleans;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnMaintenanceSession(Guid id, AnnMaintenanceRequest request,
    string principalId, AnnSeed upper, NativeAnnManifest? original, NativeAnnMaintenanceMemoryLease memory)
{
    private NativeAnnStageObservation? observation;

    internal void ObserveStage(AnnMaintenanceCapabilityKind kind, AnnWorkBudget budget)
        => Volatile.Write(ref observation, new(kind, budget));

    internal NativeAnnStageObservation? Observation => Volatile.Read(ref observation);

    private const long EmptyBytes = 0;
    internal Guid Id { get; } = id;
    internal AnnMaintenanceRequest Request { get; } = request;
    internal string PrincipalId { get; } = principalId;
    internal AnnSeed Upper { get; } = upper;
    internal NativeAnnMaintenanceMemoryLease Memory { get; } = memory;
    internal NativeAnnManifest? Manifest { get; set; } = original;
    internal PackedAnnIndex? LoadedIndex { get; set; }
    internal NativeAnnReplay? Replay { get; set; }
    internal AnnSeed? Verified { get; set; }
    internal ProjectionBatch? LastPage { get; set; }
    internal bool ExplicitBuild { get; set; } = request.Mode == AnnMaintenanceMode.Build;
    internal long RetainedBytes => checked(Upper.OwnedBytesUpperBound
        + (Replay?.RetainedBytes ?? EmptyBytes) + (Verified?.OwnedBytesUpperBound ?? EmptyBytes)
        + (LoadedIndex?.RetainedBytesUpperBound ?? EmptyBytes));

    internal void Require(Guid id, AnnMaintenanceRequest request, string principal)
    {
        if (id != Id || principal != PrincipalId
            || !NativeSerialization.Serialize(request).AsSpan().SequenceEqual(NativeSerialization.Serialize(Request)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, NativeAnnProtocol.Ownership); }
    }

    internal void RequireUpper(AnnSeed current)
    {
        NativeAnnCanonicalSource.RequireRequest(Request, current);
        if (current.DependencySha256 != Upper.DependencySha256 || current.Cut.OutboxTail != Upper.Cut.OutboxTail
            || current.CorpusSha256 != Upper.CorpusSha256 || current.Scope.SchemaVersion != Upper.Scope.SchemaVersion
            || current.Scope.PolicyEpoch != Upper.Scope.PolicyEpoch)
        { throw Errors.Fail(ErrorCode.HistoryUnavailable, NativeAnnProtocol.MissingDependencyHistory); }
    }
}
