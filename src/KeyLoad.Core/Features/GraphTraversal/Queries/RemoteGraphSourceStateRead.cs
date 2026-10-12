using System.Collections.Immutable;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const long RemoteGraphAbsentRevision = 0;

    internal RemoteGraphSourceSnapshotV1 ReadRemoteGraphSourceState(string principalId,
        RemoteGraphSourceReadRequestV1 request, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        if (request.SourceOwner is null)
        { throw Errors.Fail(ErrorCode.Validation, RemoteGraphProtocol.Invalid); }
        if (request.Delivery is not null)
        {
            if (request.Original is not null || request.OriginalCommandId != Guid.Empty
                || request.Delivery.CommandId == Guid.Empty || request.Delivery.ExpectedRevision < RemoteGraphFirstGeneration
                || request.Delivery.SourcePartition != request.SourcePartition || request.Delivery.Graph != request.Graph
                || request.Delivery.EdgeId != request.EdgeId)
            { throw Errors.Fail(ErrorCode.Validation, RemoteGraphProtocol.Invalid); }
            ValidatePartition(request.SourcePartition);
            JsonData.Identifier(request.Graph);
            JsonData.Identifier(request.EdgeId);
            var delivery = Store.Read(view => CaptureRemoteGraphDeliverySourceState(work.CreateView(view), principalId, request, work));
            work.CheckResult(delivery);
            return delivery;
        }
        if (request.OriginalCommandId == Guid.Empty || request.Original is null
            || request.Original.CommandId != request.OriginalCommandId
            || request.Original.SourcePartition != request.SourcePartition
            || (request.Original.Upsert is null) == (request.Original.Delete is null)
            || (request.Original.Upsert?.Graph ?? request.Original.Delete?.Graph) != request.Graph
            || (request.Original.Upsert?.EdgeId ?? request.Original.Delete?.EdgeId) != request.EdgeId)
        { throw Errors.Fail(ErrorCode.Validation, RemoteGraphProtocol.Invalid); }
        ValidatePartition(request.SourcePartition);
        JsonData.Identifier(request.Graph);
        JsonData.Identifier(request.EdgeId);
        var result = Store.Read(view => CaptureRemoteGraphSourceState(work.CreateView(view), principalId,
            request, work));
        work.CheckResult(result);
        return result;
    }

    private RemoteGraphSourceSnapshotV1 CaptureRemoteGraphSourceState(IKeyValueView view, string principalId,
        RemoteGraphSourceReadRequestV1 request, ReadExecutionBudget work)
    {
        var principal = Principal(view, principalId, EvaluationClock.GetUtcNow());
        Authorization.Require(principal, request.SourcePartition, request.Graph, Capability.GraphWrite);
        var graph = Resource(view, request.SourcePartition, request.Graph, ResourceKind.Graph);
        var cut = CaptureRemoteGraphOwnerCut(view, principal, request.SourcePartition,
            request.SourceOwner, graph.SchemaVersion);
        var edge = view.GetRecord<EdgeRecord>(EdgeKey(request.SourcePartition, request.Graph, request.EdgeId));
        if (edge is not null)
        {
            ValidateCanonicalEdge(edge, request.SourcePartition, request.EdgeId);
            _ = VisibleVertex(view, principal, edge.From, work);
        }
        if (request.Original?.Upsert is not null)
        {
            _ = RequireRemoteGraphSource(view, principal, new RemoteGraphSourceMutationV1(
                RemoteGraphProtocol.Version, request.Original, RemoteGraphAbsentRevision, [], edge));
        }
        var ownerVersion = CurrentOwnerVersion(view, request.SourcePartition, request.Graph, request.EdgeId, edge);
        var intents = ImmutableArray.CreateBuilder<RemoteGraphIntentV1>();
        var scan = view.VisitRange(RemoteGraphKeys.IntentPrefix(request.SourcePartition, request.Graph,
            request.EdgeId), Limits.MaxScanRecords, (key, bytes) =>
        {
            work.Check();
            var intent = GraphCrossPartitionRecords.Decode<RemoteGraphIntentV1>(bytes);
            RequireRemoteGraphStoredIntent(view, request, intent);
            if (!key.SequenceEqual(RemoteGraphKeys.Intent(request.SourcePartition, request.Graph,
                request.EdgeId, intent.Content.Destination)))
            { throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid); }
            intents.Add(intent);
            return true;
        });
        if (scan.HasMore)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteGraphProtocol.Invalid); }
        return new(RemoteGraphProtocol.Version, request, edge,
            ownerVersion?.Revision ?? edge?.Revision ?? RemoteGraphAbsentRevision, intents.ToImmutable(), cut,
            ReadRemoteGraphOriginalOutcome(view, principal, request));
    }
}
