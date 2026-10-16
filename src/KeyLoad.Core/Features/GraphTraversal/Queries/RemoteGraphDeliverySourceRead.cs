using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private RemoteGraphSourceSnapshotV1 CaptureRemoteGraphDeliverySourceState(IKeyValueView view,
        string principalId, RemoteGraphSourceReadRequestV1 request, ReadExecutionBudget work)
    {
        var principal = Principal(view, principalId, EvaluationClock.GetUtcNow());
        Authorization.Require(principal, request.SourcePartition, request.Graph, Capability.GraphWrite);
        var graph = BindAuthorizedReadResource(view, request.SourcePartition, request.Graph, ResourceKind.Graph);
        _ = CaptureRemoteGraphOwnerCut(view, principal, request.SourcePartition, request.SourceOwner, graph.SchemaVersion);
        var intent = ResolveRemoteGraphDeliveryIntent(view, request, work);
        RequireRemoteGraphStoredIntent(view, request, intent);
        ObserveRemoteGraphReadIntent(view, intent, work, target: false);
        _ = VisibleVertex(view, principal, intent.Content.Edge.From, work);
        var original = ResolveRemoteGraphDeliveryOriginal(view, principal, request, intent);
        var resolved = request with
        {
            OriginalCommandId = intent.SourceCommandId,
            Original = original,
            Delivery = null
        };
        var result = CaptureRemoteGraphSourceState(view, principalId, resolved, work);
        if (result.OriginalOutcome is null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteGraphProtocol.Invalid);
        }
        return result with
        {
            Request = request
        };
    }

    private RemoteGraphIntentV1 ResolveRemoteGraphDeliveryIntent(IKeyValueView view,
        RemoteGraphSourceReadRequestV1 request, ReadExecutionBudget work)
    {
        var delivery = request.Delivery!;
        var pending = GraphCrossPartitionRecords.Read<RemoteGraphIntentV1>(view,
            RemoteGraphKeys.Intent(request.SourcePartition, request.Graph, request.EdgeId, delivery.Destination));
        if (pending?.Content?.Revision == delivery.ExpectedRevision)
        {
            _ = RemoteGraphLifetimeAdmission.BindOriginalDestination(pending, delivery.Destination);
            return pending;
        }
        RemoteGraphIntentV1? found = null;
        var prefix = RemoteGraphKeys.CompletionPrefix(request.SourcePartition, request.Graph, request.EdgeId, delivery.Destination);
        var scan = view.VisitRange(prefix, Limits.MaxScanRecords, (key, bytes) =>
        {
            work.Check();
            var completed = GraphCrossPartitionRecords.Decode<RemoteGraphCompletionResultV1>(bytes);
            var receipt = completed?.Original?.Receipt;
            if (receipt?.Intent?.Content is null || completed?.Receipt is null)
            {
                throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
            }
            var intent = receipt.Intent;
            RequireRemoteGraphStoredIntent(view, request, intent);
            if (RemoteGraphLifetimeAdmission.BindOriginalDestination(intent, delivery.Destination) != intent.Content.Destination
                || !key.SequenceEqual(RemoteGraphKeys.Completion(request.SourcePartition, request.Graph,
                    request.EdgeId, delivery.Destination, intent.Content.Fingerprint)))
            {
                throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
            }
            if (intent.Content.Revision == delivery.ExpectedRevision)
            {
                if (found is not null)
                {
                    throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
                }
                found = intent;
            }
            return true;
        });
        if (scan.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteGraphProtocol.Invalid);
        }
        return found ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteGraphProtocol.Invalid);
    }

    private RemoteGraphSourceIdentityV1 ResolveRemoteGraphDeliveryOriginal(IKeyValueView view,
        PrincipalRecord principal, RemoteGraphSourceReadRequestV1 request, RemoteGraphIntentV1 intent)
    {
        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, request.SourcePartition);
        var selected = CommandOutcomeKeyResolver.Select(view, principal.Id, intent.SourceCommandId, scope);
        var outcome = selected.Outcome ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteGraphProtocol.Invalid);
        if (outcome.PolicyEpoch != principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ChangedOutcomePrincipalPolicyMessage);
        }
        if (outcome.Incarnation != Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, EarlierOutcomeIncarnationMessage);
        }
        if (outcome.Result is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
        }
        if (outcome.Result.Error is not null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteGraphProtocol.Invalid);
        }
        var retained = outcome.Result.Get<RemoteGraphSourceResultV1>();
        if (retained?.Original?.Original is null || retained.Receipt is null
            || retained.Receipt.CommandId != intent.SourceCommandId || retained.Intents.IsDefault
            || retained.Intents.Count(value => NativeSerialization.Serialize(value).AsSpan().SequenceEqual(
                NativeSerialization.Serialize(intent))) != RemoteGraphSinglePreparation)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
        }
        var original = retained.Original.Original;
        if (original.CommandId != intent.SourceCommandId || original.SourcePartition != request.SourcePartition
            || (original.Upsert is null) == (original.Delete is null)
            || (original.Upsert?.Graph ?? original.Delete?.Graph) != request.Graph
            || (original.Upsert?.EdgeId ?? original.Delete?.EdgeId) != request.EdgeId)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
        }
        return original;
    }
}
