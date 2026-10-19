using KeyLoad.Clustering;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;

namespace KeyLoad.Server.Features.Search;

internal sealed class AnnMaintenanceSession(Guid id, AnnMaintenanceRequest request,
    string principalId, AnnSeed upper, AnnManifest? original, AnnMaintenanceMemoryLease memory,
    MaintenanceSessionWork work)
{
    private AnnStageObservation? observation;
    internal MaintenanceSessionWork Work { get; } = work;

    internal void ObserveStage(AnnMaintenanceCapabilityKind kind, AnnWorkBudget budget)
        => Volatile.Write(ref observation, new(kind, budget));

    internal AnnStageObservation? Observation => Volatile.Read(ref observation);

    private const long EmptyBytes = 0;
    internal Guid Id { get; } = id;
    internal AnnMaintenanceRequest Request { get; } = request;
    internal string PrincipalId { get; } = principalId;
    internal AnnSeed Upper { get; } = upper;
    internal AnnMaintenanceMemoryLease Memory { get; } = memory;
    internal AnnManifest? Manifest { get; set; } = original;
    internal PackedAnnIndex? LoadedIndex
    {
        get; set;
    }
    internal AnnReplay? Replay
    {
        get; set;
    }
    internal AnnSeed? Verified
    {
        get; set;
    }
    internal ProjectionBatch? LastPage
    {
        get; set;
    }
    internal bool ExplicitBuild { get; set; } = request.Mode == AnnMaintenanceMode.Build;
    internal long RetainedBytes => checked(Upper.OwnedBytesUpperBound
        + (Replay?.RetainedBytes ?? EmptyBytes) + (Verified?.OwnedBytesUpperBound ?? EmptyBytes)
        + (LoadedIndex?.RetainedBytesUpperBound ?? EmptyBytes));

    internal void Require(Guid id, AnnMaintenanceRequest request, string principal)
    {
        if (id != Id || principal != PrincipalId
            || !AnnOriginalResults.SameRequest(request, Request))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, AnnProtocol.Ownership);
        }
    }

    internal void RequireUpper(AnnSeed current)
    {
        AnnCanonicalSource.RequireRequest(Request, current);
        if (current.DependencySha256 != Upper.DependencySha256 || current.Cut.OutboxTail != Upper.Cut.OutboxTail
            || current.CorpusSha256 != Upper.CorpusSha256 || current.Scope.SchemaVersion != Upper.Scope.SchemaVersion
            || current.Scope.PolicyEpoch != Upper.Scope.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, AnnProtocol.MissingDependencyHistory);
        }
    }
}
