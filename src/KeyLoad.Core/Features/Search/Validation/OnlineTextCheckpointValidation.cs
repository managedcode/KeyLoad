using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void ValidateOnlineTextCheckpoint(IKeyValueView view, PrincipalRecord principal,
        OnlineTextIndexMaintenanceRequest request, ProjectionBatchResult acknowledged,
        CommitProjectionBatchRequest? intent)
    {
        if (acknowledged.Receipt.CommandId == Guid.Empty || !acknowledged.Receipt.Mutations.IsEmpty)
        { throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.InvalidPublication); }
        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, request.Consumer.Partition);
        var outcome = CommandOutcomeKeyResolver.Select(view, principal.Id, acknowledged.Receipt.CommandId, scope).Outcome
            ?? throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.MissingAuthority);
        if (outcome.Incarnation != Store.Identity.Incarnation || outcome.PolicyEpoch != principal.PolicyEpoch
            || outcome.Result.Error is not null)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, OnlineTextPublicationProtocol.MissingAuthority); }
        if (intent is not null)
        {
            if (intent.CommandId != acknowledged.Receipt.CommandId || intent.Consumer != request.Consumer
                || !intent.Effects.IsEmpty)
            { throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.InvalidPublication); }
            var payload = NativeSerialization.Serialize(intent);
            RequireNativeBudget(payload.Length);
            var identity = new ReplicatedOperation(intent.CommandId, OperationKind.CommitProjectionBatch,
                principal.Id, EvaluationClock.GetUtcNow(), Identity<CommitProjectionBatchRequest>(payload));
            if (outcome.Fingerprint != CommandFingerprint(identity))
            { throw Errors.Fail(ErrorCode.Conflict, OnlineTextPublicationProtocol.ChangedOriginal); }
        }
        var actual = outcome.Result.Get<ProjectionBatchResult>();
        RequireNativeBudget(NativeSerialization.Measure(actual));
        RequireNativeBudget(NativeSerialization.Measure(acknowledged));
        if (!NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(NativeSerialization.Serialize(acknowledged))
            || acknowledged.Checkpoint != RequireActiveProjectionConsumer(view, principal,
                request.Consumer, request.ConsumerGeneration).Checkpoint)
        { throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.InvalidPublication); }
    }

}
