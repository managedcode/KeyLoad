using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Core.Features.GraphTraversal.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal RemoteGraphReceiverCandidatesV1 ReadRemoteGraphReceiverCandidates(string principalId,
        RemoteGraphReceiverCandidatesRequestV1 request, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(work);
        GraphCrossPartitionValidation.ValidateIncoming(new(GraphCrossPartitionProtocol.CurrentVersion,
            request.Target, request.Graph, request.Limit), Limits, work);
        var result = Store.Read(view => CaptureRemoteGraphReceiverCandidates(work.CreateView(view),
            principalId, request, work));
        work.CheckResult(result);
        return result;
    }

    private RemoteGraphReceiverCandidatesV1 CaptureRemoteGraphReceiverCandidates(IKeyValueView view,
        string principalId, RemoteGraphReceiverCandidatesRequestV1 request, ReadExecutionBudget work)
    {
        var principal = Principal(view, principalId, EvaluationClock.GetUtcNow());
        Authorization.Require(principal, request.Target.Partition, request.Graph, Capability.GraphRead);
        var graph = BindAuthorizedReadResource(view, request.Target.Partition, request.Graph, ResourceKind.Graph);
        var admitted = request with
        {
            Target = VisibleVertex(view, principal, request.Target).Reference
        };
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        var cut = CaptureRemoteGraphOwnerCut(view, principal, request.Target.Partition,
            catalog.DefaultShard, graph.SchemaVersion);
        var states = ImmutableArray.CreateBuilder<RemoteGraphReceiverStateV1>();
        var retainedBytes = work.MeasureResult(new RemoteGraphReceiverCandidatesV1(
            RemoteGraphProtocol.Version, request, [], cut));
        var scan = view.VisitRange(RemoteGraphKeys.ReceiverPrefix(request.Target, request.Graph),
            Limits.MaxScanRecords, (key, bytes) =>
        {
            work.Check();
            var state = GraphCrossPartitionRecords.Decode<RemoteGraphReceiverStateV1>(bytes);
            RequireRemoteGraphIncomingState(view, admitted, state, work);
            if (!key.SequenceEqual(RemoteGraphKeys.Receiver(request.Target, request.Graph,
                state.Content.SourcePartition, state.Content.EdgeId)))
            {
                throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
            }
            var addition = checked(work.MeasureResult(state) + (states.Count == RemoteGraphProtocol.EmptyCount ? RemoteGraphProtocol.EmptyCount : RemoteGraphProtocol.ItemSeparatorBytes));
            retainedBytes = checked(retainedBytes + addition);
            if (retainedBytes > Limits.MaxBatchBytes || retainedBytes > work.MaximumResultBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteGraphProtocol.Invalid);
            }
            states.Add(state);
            return true;
        });
        if (scan.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteGraphProtocol.Invalid);
        }
        return new(RemoteGraphProtocol.Version, request, states.ToImmutable(), cut);
    }
}
