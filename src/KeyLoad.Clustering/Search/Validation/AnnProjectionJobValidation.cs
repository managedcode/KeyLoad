using KeyLoad.Core.Features.Search;

namespace KeyLoad.Clustering;

internal static class AnnProjectionJobValidation
{
    internal static AnnMaintenanceRequest Initial(AnnProjectionWork work)
    {
        var definition = work.Consumer.Definition;
        var profile = definition.AnnMaintenance
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProjectionJobProtocol.Invalid);
        return new(Guid.NewGuid(), work.Consumer.Consumer, profile.Collection, profile.Field, profile.Space,
            definition.IndexGeneration, work.NodeId, work.Placement, AnnMaintenanceMode.Build)
        {
            MaintenanceProfile = profile
        };
    }

    internal static void RequireCurrent(AnnProjectionJobState original, AnnProjectionWork work)
    {
        var request = original.PendingRequest ?? original.LastRequest ?? original.InitialRequest;
        if (original.Authority != work.Consumer.MaintenanceAuthority
            || request.Consumer != work.Consumer.Consumer
            || request.IndexGeneration != work.Consumer.Definition.IndexGeneration
            || request.MaintenanceProfile != work.Consumer.Definition.AnnMaintenance)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProjectionJobProtocol.Invalid);
        }
        if (request.NodeId != work.NodeId || request.Placement.PhysicalShardId != work.Placement.PhysicalShardId
            || request.Placement.Incarnation != work.Placement.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionJobProtocol.Invalid);
        }
    }

    internal static void RequireResult(AnnMaintenanceRequest request, long horizon, AnnMaintenanceResult result,
        AnnProjectionPublication published)
    {
        if (result.CommandId != request.CommandId || result.Consumer != request.Consumer
            || result.IndexGeneration != request.IndexGeneration || result.Phase != AnnMaintenancePhase.Completed
            || result.Source is null || result.Source.ThroughSequence < horizon
            || published.Consumer != request.Consumer || published.IndexGeneration != request.IndexGeneration
            || !SameLineage(published.Source, result.Source) || published.Count != result.Count
            || published.IndexSha256 != result.IndexSha256)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, AnnProjectionJobProtocol.Unresolved);
        }
    }

    private static bool SameLineage(AnnSourceCut published, AnnSourceCut result)
        => published.NodeId == result.NodeId && published.Incarnation == result.Incarnation
            && published.ThroughSequence == result.ThroughSequence
            && published.ReadGeneration <= result.ReadGeneration && published.SchemaVersion == result.SchemaVersion
            && published.PolicyEpoch == result.PolicyEpoch && published.CorpusSha256 == result.CorpusSha256
            && published.DependencySha256 == result.DependencySha256
            && published.StoreFormatVersion == result.StoreFormatVersion
            && published.KeyCodecVersion == result.KeyCodecVersion
            && published.OutboxFirstAvailable == result.OutboxFirstAvailable
            && published.Position <= result.Position && published.AppliedPosition <= result.AppliedPosition;
}
