using System.Collections.Immutable;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Core.Features.GraphTraversal.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal RemoteIncomingGraphEdgesPageV1 ReadRemoteGraphIncomingFinalize(string principalId,
        RemoteGraphIncomingFinalizeRequestV1 request, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(work);
        if (request.Candidates?.Request is null || request.Candidates.States.IsDefault
            || request.Candidates.Version != RemoteGraphProtocol.Version || request.Proofs.IsDefault
            || request.Proofs.Length > request.Candidates.States.Length)
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteGraphProtocol.Invalid);
        }
        GraphCrossPartitionValidation.ValidateIncoming(new(GraphCrossPartitionProtocol.CurrentVersion,
            request.Candidates.Request.Target, request.Candidates.Request.Graph,
            request.Candidates.Request.Limit), Limits, work);
        var result = Store.Read(view => FinalizeRemoteGraphIncoming(work.CreateView(view),
            principalId, request, work));
        work.CheckResult(result);
        return result;
    }

    private RemoteIncomingGraphEdgesPageV1 FinalizeRemoteGraphIncoming(IKeyValueView view,
        string principalId, RemoteGraphIncomingFinalizeRequestV1 request, ReadExecutionBudget work)
    {
        var expected = request.Candidates;
        var actual = CaptureRemoteGraphReceiverCandidates(view, principalId, expected.Request, work);
        if (!RemoteGraphIncomingBytesEqual(actual, expected, work))
        {
            throw Errors.Fail(ErrorCode.Conflict, RemoteGraphProtocol.Invalid);
        }
        var principal = Principal(view, principalId, EvaluationClock.GetUtcNow());
        var graph = Resource(view, actual.Request.Target.Partition, actual.Request.Graph, ResourceKind.Graph);
        var proofs = IndexRemoteGraphIncomingProofs(request, work);
        var rows = ImmutableArray.CreateBuilder<RemoteIncomingGraphEdgeRowV1>();
        var retainedBytes = work.MeasureResult(new RemoteIncomingGraphEdgesPageV1(
            RemoteGraphProtocol.Version, [], actual.TargetCut, RemoteGraphProtocol.EventualProjection));
        foreach (var state in actual.States)
        {
            work.Check();
            var locator = (state.Content.SourcePartition, state.Content.EdgeId);
            if (!proofs.Remove(locator, out var proof))
            {
                continue;
            }
            RequireRemoteGraphIncomingProof(state, proof, work);
            if (state.Content.Deleted || proof.Edge is null)
            {
                continue;
            }
            if (rows.Count >= actual.Request.Limit)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteGraphProtocol.Invalid);
            }
            var edge = proof.Edge with
            {
                AttributesJson = Authorization.Project(principal, graph.FieldPolicies,
                    proof.Edge.AttributesJson, out _)
            };
            var row = new RemoteIncomingGraphEdgeRowV1(edge, state.Content.Revision, proof.SourceCut);
            retainedBytes = checked(retainedBytes + work.MeasureResult(row) + (rows.Count == RemoteGraphProtocol.EmptyCount ? RemoteGraphProtocol.EmptyCount : RemoteGraphProtocol.ItemSeparatorBytes));
            if (retainedBytes > Limits.MaxBatchBytes || retainedBytes > work.MaximumResultBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteGraphProtocol.Invalid);
            }
            rows.Add(row);
        }
        if (proofs.Count != RemoteGraphProtocol.EmptyCount)
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteGraphProtocol.Invalid);
        }
        return new(RemoteGraphProtocol.Version, rows.ToImmutable(), actual.TargetCut,
            RemoteGraphProtocol.EventualProjection);
    }
}
