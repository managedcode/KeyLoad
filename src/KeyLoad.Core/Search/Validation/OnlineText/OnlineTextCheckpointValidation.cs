using KeyLoad.Core.CommandOutcomes.Contracts;
using KeyLoad.Core.CommandOutcomes.Execution;
using KeyLoad.Core.Search;
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
        if (outcome.Incarnation != engine.Store.Identity.Incarnation || outcome.PolicyEpoch != principal.PolicyEpoch || Errors.Code(outcome.Result /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */.Problem).Value is not null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, OnlineTextPublicationProtocol.MissingAuthority);
        }

        if (intent is not null)
        {
            if (intent.CommandId != acknowledged.Receipt.CommandId || intent.Consumer != request.Consumer || !intent.Effects.IsEmpty)
            {
                throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.InvalidPublication);
            }

            if (!(DatabaseCommandContentIdentity.SameInput(outcome.PreparedCommand, OperationKind.CommitProjectionBatch, intent) /* KLD0041: A retry compares the public shape of the Kind-selected native inputs while preserving their stored type and JSON rules. */))
            {
                throw Errors.Fail(ErrorCode.Conflict, OnlineTextPublicationProtocol.ChangedOriginal);
            }
        }

        var actual = outcome.Result /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */.Get<ProjectionBatchResult>() /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */;
        if (!OrleansSerialization.Serialize(actual).AsSpan().SequenceEqual(OrleansSerialization.Serialize(acknowledged)) || acknowledged.Checkpoint != ProjectionConsumerAuthority.RequireActiveProjectionConsumer(view, principal, request.Consumer, request.ConsumerGeneration).Checkpoint)
        {
            throw Errors.Fail(ErrorCode.Corruption, OnlineTextPublicationProtocol.InvalidPublication);
        }
    }
}
