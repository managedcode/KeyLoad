using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.CommandOutcomes.Execution;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes OnlineTextCheckpointValidation operations against the caller's database context.</summary>
public static class OnlineTextCheckpointValidation
{
    internal static void ValidateOnlineTextCheckpoint(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, OnlineTextIndexMaintenanceRequest request, ProjectionBatchResult acknowledged, CommitProjectionBatchRequest? intent)
    {
        if (acknowledged.Receipt.CommandId == Guid.Empty || !acknowledged.Receipt.Mutations.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.InvalidPublication);
        }

        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, request.Consumer.Partition);
        var outcome = CommandOutcomeKeyResolver.Select(view, principal.Id, acknowledged.Receipt.CommandId, scope).Outcome ?? throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.MissingAuthority);
        if (outcome.Incarnation != engine.Store.Identity.Incarnation || outcome.PolicyEpoch != principal.PolicyEpoch || Errors.Code(outcome.Result.Problem) is not null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, OnlineTextPublicationProtocol.MissingAuthority);
        }

        if (intent is not null)
        {
            if (intent.CommandId != acknowledged.Receipt.CommandId || intent.Consumer != request.Consumer || !intent.Effects.IsEmpty)
            {
                throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.InvalidPublication);
            }

            var payload = OrleansSerialization.Serialize(intent);
            engine.RequireBudget(payload.Length);
            var fingerprint = DatabaseCommandFingerprint.Compute(intent.CommandId, OperationKind.CommitProjectionBatch, principal.Id, CommandExecutionExecution.Identity<CommitProjectionBatchRequest>(payload));
            if (outcome.Fingerprint != fingerprint)
            {
                throw Errors.Fail(ErrorCode.Conflict, OnlineTextPublicationProtocol.ChangedOriginal);
            }
        }

        var actual = outcome.Result.Get<ProjectionBatchResult>();
        engine.RequireBudget(OrleansSerialization.Measure(actual));
        engine.RequireBudget(OrleansSerialization.Measure(acknowledged));
        if (!OrleansSerialization.Serialize(actual).AsSpan().SequenceEqual(OrleansSerialization.Serialize(acknowledged)) || acknowledged.Checkpoint != ProjectionConsumerAuthority.RequireActiveProjectionConsumer(view, principal, request.Consumer, request.ConsumerGeneration).Checkpoint)
        {
            throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.InvalidPublication);
        }
    }
}
