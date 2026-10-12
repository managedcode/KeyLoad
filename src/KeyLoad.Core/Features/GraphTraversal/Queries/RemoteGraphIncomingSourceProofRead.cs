using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal RemoteGraphIncomingSourceProofV1 ReadRemoteGraphIncomingSourceProof(string principalId,
        RemoteGraphIncomingSourceProofRequestV1 request, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(work);
        var intent = request.Intent;
        if (intent?.Content is null || intent.SourceOwner is null)
        { throw Errors.Fail(ErrorCode.Validation, RemoteGraphProtocol.Invalid); }
        ValidatePartition(intent.Content.SourcePartition);
        JsonData.Identifier(intent.Content.Graph);
        JsonData.Identifier(intent.Content.EdgeId);
        work.Check();
        var result = Store.Read(view => CaptureRemoteGraphIncomingSourceProof(work.CreateView(view),
            principalId, intent, work));
        work.CheckResult(result);
        return result;
    }

    private RemoteGraphIncomingSourceProofV1 CaptureRemoteGraphIncomingSourceProof(IKeyValueView view,
        string principalId, RemoteGraphIntentV1 intent, ReadExecutionBudget work)
    {
        var content = intent.Content;
        var principal = Principal(view, principalId, EvaluationClock.GetUtcNow());
        Authorization.Require(principal, content.SourcePartition, content.Graph, Capability.GraphRead);
        var graph = Resource(view, content.SourcePartition, content.Graph, ResourceKind.Graph);
        RequireRemoteGraphStoredIntent(view, content.SourcePartition, content.Graph, content.EdgeId,
            intent.SourceOwner, intent);
        _ = VisibleVertex(view, principal, content.Edge.From);
        var cut = CaptureRemoteGraphOwnerCut(view, principal, content.SourcePartition,
            intent.SourceOwner, graph.SchemaVersion);
        var edge = view.GetRecord<EdgeRecord>(EdgeKey(content.SourcePartition, content.Graph, content.EdgeId));
        if (edge is not null)
        { ValidateCanonicalEdge(edge, content.SourcePartition, content.EdgeId); }
        var owner = CurrentOwnerVersion(view, content.SourcePartition, content.Graph, content.EdgeId, edge)
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
        if (owner.Revision < content.Revision)
        { throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid); }
        if (content.Deleted || owner.Deleted || owner.Revision > content.Revision || edge is null)
        { return new(RemoteGraphProtocol.Version, intent, null, cut); }
        if (edge.To != content.Destination || GraphCrossPartitionRecords.Fingerprint(content.SourcePartition,
            content.Graph, content.EdgeId, content.Destination, content.Revision, false, edge) != content.Fingerprint)
        { throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid); }
        _ = VisibleVertex(view, principal, edge.From);
        work.Check();
        var projected = edge with
        {
            AttributesJson = Authorization.Project(principal, graph.FieldPolicies, edge.AttributesJson, out _)
        };
        return new(RemoteGraphProtocol.Version, intent, projected, cut);
    }
}
