using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Core.Features.GraphTraversal.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.GraphTraversal;

internal sealed class GraphIncomingEdgesReader(DatabaseEngine database, IKeyValueView view,
    PrincipalRecord principal, ReadIncomingGraphEdgesRequestV1 request, ReadExecutionBudget budget,
    ReadExecutionBudgetReadGrant grant, long cutPosition)
{
    private const string RangeExceeded = "The graph incoming-edge scan exceeds its bound.";
    private const string InvalidProjection = "A graph reverse projection is inconsistent.";
    private ResourceDefinition targetGraph = null!;

    internal GraphIncomingEdgesPageV1 Read()
    {
        targetGraph = RequireGraphRead(request.Target.Partition);
        RequireVisible(request.Target);
        var page = new GraphIncomingEdgesPageBuilder(database, principal, request.Limit, budget, cutPosition);
        var local = grant.VisitRange(view,
            GraphCrossPartitionKeys.LocalIncomingPrefix(request.Target, request.Graph),
            database.Limits.MaxScanRecords, (key, value) => VisitLocal(page, key, value));
        if (local.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RangeExceeded);
        }
        var scan = grant.VisitRange(view, GraphCrossPartitionKeys.ReversePrefix(request.Target, request.Graph),
            database.Limits.MaxScanRecords, (key, value) => VisitRemote(page, key, value));
        if (scan.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RangeExceeded);
        }
        return page.Build();
    }

    private bool VisitLocal(GraphIncomingEdgesPageBuilder page, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        budget.Check();
        var edgeId = NativeSerialization.Deserialize<string>(value);
        if (edgeId is null || !key.SequenceEqual(GraphCrossPartitionKeys.LocalIncoming(
                request.Target.Partition, request.Graph, request.Target, edgeId)))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidProjection);
        }
        var edge = GraphCrossPartitionRecords.Read<EdgeRecord>(view,
            GraphCrossPartitionKeys.CanonicalEdge(request.Target.Partition, request.Graph, edgeId), grant)
            ?? throw Errors.Fail(ErrorCode.Corruption, InvalidProjection);
        GraphCrossPartitionValidation.ValidateEdge(edge, database.Limits);
        if (edge.Id != edgeId || edge.From.Partition != request.Target.Partition
            || edge.To != request.Target)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidProjection);
        }
        if (!TryRequireVisible(edge.From))
        {
            return true;
        }
        page.Add(edge, 0, targetGraph);
        return true;
    }

    private bool VisitRemote(GraphIncomingEdgesPageBuilder page, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        budget.Check();
        var state = GraphCrossPartitionRecords.Decode<GraphCrossPartitionReceiverStateV1>(value)
            ?? throw Errors.Fail(ErrorCode.Corruption, InvalidProjection);
        GraphCrossPartitionValidation.ValidateReceiver(state, state.SourcePartition, request.Graph,
            state.EdgeId, request.Target);
        if (!key.SequenceEqual(GraphCrossPartitionKeys.Reverse(state.Destination.Partition,
                request.Graph, request.Target, state.SourcePartition, state.EdgeId)))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidProjection);
        }
        if (state.Deleted)
        {
            return true;
        }
        var sourceGraph = TryRequireSourceGraphRead(state.SourcePartition);
        if (sourceGraph is null)
        {
            return true;
        }
        DatabaseEngine.RequireSameGraphOwner(view, state.SourcePartition, request.Target.Partition, grant);
        var owner = GraphCrossPartitionRecords.Read<GraphEdgeOwnerVersionV1>(view,
            GraphCrossPartitionKeys.OwnerVersion(state.SourcePartition, request.Graph, state.EdgeId), grant)
            ?? throw Errors.Fail(ErrorCode.Corruption, InvalidProjection);
        GraphCrossPartitionValidation.ValidateOwnerVersion(owner);
        if (owner.Revision > state.Revision || owner.Deleted)
        {
            return true;
        }
        if (owner.Revision != state.Revision)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidProjection);
        }
        var edge = GraphCrossPartitionRecords.Read<EdgeRecord>(view,
            GraphCrossPartitionKeys.CanonicalEdge(state.SourcePartition, request.Graph, state.EdgeId), grant);
        if (!IsCurrentEdge(state, owner, edge))
        {
            return true;
        }
        if (!TryRequireVisible(edge!.From))
        {
            return true;
        }
        page.Add(edge, edge.Revision, sourceGraph);
        return true;
    }

    private ResourceDefinition RequireGraphRead(PartitionRef partition)
    {
        AuthorizationRequire(partition, Capability.GraphRead);
        return GraphIncomingReadScope.ReadGraphResource(view, grant, partition, request.Graph);
    }

    private ResourceDefinition? TryRequireSourceGraphRead(PartitionRef partition)
    {
        try
        {
            return RequireGraphRead(partition);
        }
        catch (KeyLoadException error) when (error.Code is ErrorCode.PermissionDenied or ErrorCode.NotFound)
        {
            return null;
        }
    }

    private void RequireVisible(EntityRef entity)
    {
        if (!TryRequireVisible(entity))
        {
            throw Errors.Fail(ErrorCode.NotFound, "The graph vertex is unavailable.");
        }
    }

    private bool TryRequireVisible(EntityRef entity)
    {
        try
        {
            AuthorizationRequire(entity.Partition, Capability.DocumentsRead, entity.Collection);
            _ = GraphIncomingReadScope.ReadCollection(view, grant, entity.Partition, entity.Collection);
            var document = GraphIncomingReadScope.ReadDocument(view, grant, entity);
            return document is not null && !document.Deleted
                && database.Authorization.CanReadRow(principal, document.Access);
        }
        catch (KeyLoadException error) when (error.Code is ErrorCode.PermissionDenied or ErrorCode.NotFound)
        {
            return false;
        }
    }

    private void AuthorizationRequire(PartitionRef partition, Capability capability,
        string? resource = null)
        => database.Authorization.Require(principal, partition, resource ?? request.Graph, capability);

    private bool IsCurrentEdge(GraphCrossPartitionReceiverStateV1 state,
        GraphEdgeOwnerVersionV1 owner, EdgeRecord? edge)
    {
        if (edge is null)
        {
            if (!owner.Deleted)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidProjection);
            }
            return false;
        }
        GraphCrossPartitionValidation.ValidateEdge(edge, database.Limits);
        if (edge.Id != state.EdgeId || edge.From.Partition != state.SourcePartition
            || edge.To != state.Destination || edge.Revision != state.Revision
            || owner.Deleted || owner.Revision != edge.Revision
            || GraphCrossPartitionRecords.Fingerprint(state.SourcePartition, state.Graph, state.EdgeId,
                state.Destination, state.Revision, deleted: false, edge) != state.Fingerprint)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidProjection);
        }
        return true;
    }

}
