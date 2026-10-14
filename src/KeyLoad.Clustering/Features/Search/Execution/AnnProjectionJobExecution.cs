using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal sealed class AnnProjectionJobExecution(DatabaseEngine database, INativeAnnMaintenance native,
    IOptions<DatabaseLimits> limits, TimeProvider clock, AnnProjectionJobDispatch dispatch)
{
    internal AnnProjectionWork Read(ProjectionConsumerRef consumer, long generation, CancellationToken token)
        => database.ReadAnnProjectionWork(consumer, generation,
            new ReadExecutionBudget(limits, clock, token));

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

    internal async Task<AnnProjectionJobState?> InitializeAsync(AnnProjectionWork work, CancellationToken token)
    {
        var authority = work.Consumer.MaintenanceAuthority
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProjectionJobProtocol.Invalid);
        if (work.Consumer.InitialAnnMaintenance is { } reserved)
        {
            if (!work.Consumer.InitialAnnMaintenanceSettled)
            {
                return null;
            }
            if (reserved.NodeId != work.NodeId || reserved.Placement.PhysicalShardId != work.Placement.PhysicalShardId
                || reserved.Placement.Incarnation != work.Placement.Incarnation
                || reserved.Placement.PlacementEpoch != work.Placement.PlacementEpoch
                || !reserved.Placement.VoterIds.SequenceEqual(work.Placement.VoterIds, StringComparer.Ordinal))
            {
                var ownInitial = Initial(work);
                return new(authority, ownInitial, ownInitial, null, null, null, work.ThroughSequence, null);
            }
            var enrollment = await native.ReadEnrollmentAsync(work.Principal, reserved, token).ConfigureAwait(true);
            if (enrollment is null)
            {
                return null;
            }
            var adopted = new AnnProjectionJobState(authority, enrollment.InitialRequest, null, null,
                enrollment.LastResult, null, enrollment.LastResult.Source?.ThroughSequence
                    ?? throw Errors.Fail(ErrorCode.Corruption, AnnProjectionJobProtocol.Invalid), enrollment.LastRequest);
            RequireCurrent(adopted, work);
            return adopted;
        }
        var initial = Initial(work);
        return new(authority, initial, initial, null, null, null, work.ThroughSequence, null);
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
            || request.Placement.Incarnation != work.Placement.Incarnation
            || request.Placement.PlacementEpoch != work.Placement.PlacementEpoch
            || !request.Placement.VoterIds.SequenceEqual(work.Placement.VoterIds, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionJobProtocol.Invalid);
        }
    }

    internal async Task<AnnProjectionJobState?> NextAsync(AnnProjectionJobState original,
        AnnProjectionWork work, CancellationToken token)
    {
        RequireCurrent(original, work);
        if (original.BlockedError is { } blocked)
        {
            throw Errors.Fail(blocked, AnnProjectionJobProtocol.Unresolved);
        }
        if (original.PendingRequest is not null)
        {
            return original;
        }
        var publication = await native.ReadPublishedAsync(work.Principal, original.InitialRequest, token)
            .ConfigureAwait(true);
        if (publication.Source.ThroughSequence >= work.ThroughSequence)
        {
            return null;
        }
        var restore = original.InitialRequest with
        {
            CommandId = Guid.NewGuid(),
            Mode = AnnMaintenanceMode.Restore
        };
        return original with
        {
            PendingRequest = restore,
            JobId = null,
            RequiredThroughSequence = work.ThroughSequence
        };
    }

    internal async Task<AnnMaintenanceResult> ExecuteAsync(global::Orleans.IGrainFactory grains,
        AnnProjectionJobState original, CancellationToken token)
    {
        var request = original.PendingRequest
            ?? throw Errors.Fail(ErrorCode.Validation, AnnProjectionJobProtocol.Invalid);
        var before = Read(request.Consumer, request.IndexGeneration, token);
        RequireCurrent(original, before);
        var result = await dispatch.ExecuteAsync(grains, before.Principal, request, token).ConfigureAwait(true);
        var after = Read(request.Consumer, request.IndexGeneration, token);
        RequireCurrent(original, after);
        var publication = await native.ReadPublishedAsync(after.Principal, request, token).ConfigureAwait(true);
        RequireResult(request, original.RequiredThroughSequence, result, publication);
        return result;
    }

    private static void RequireResult(AnnMaintenanceRequest request, long horizon, AnnMaintenanceResult result,
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
