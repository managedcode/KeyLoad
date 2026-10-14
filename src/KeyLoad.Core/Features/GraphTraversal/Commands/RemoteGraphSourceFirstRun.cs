using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private EdgeRecord? RequireRemoteGraphSourceFirstRun(IAtomicTransaction tx,
        RemoteGraphSourceMutationV1 command, ReplicatedOperation operation)
    {
        var original = command.Original;
        var graph = original.Upsert?.Graph ?? original.Delete!.Graph;
        var edgeId = original.Upsert?.EdgeId ?? original.Delete!.EdgeId;
        var previous = tx.GetRecord<EdgeRecord>(EdgeKey(original.SourcePartition, graph, edgeId));
        if (previous is not null)
        {
            ValidateCanonicalEdge(previous, original.SourcePartition, edgeId);
        }
        if ((previous is null) != (command.PreviousEdge is null)
            || previous is not null && !NativeSerialization.Serialize(previous).AsSpan().SequenceEqual(
                NativeSerialization.Serialize(command.PreviousEdge!)))
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RemoteGraphProtocol.Invalid);
        }
        var ownerVersion = CurrentOwnerVersion(tx, original.SourcePartition, graph, edgeId, previous);
        var currentRevision = ownerVersion?.Revision ?? previous?.Revision ?? RemoteGraphAbsentRevision;
        CheckRevision(currentRevision, command.ExpectedOwnerRevision);
        CheckRevision(currentRevision, original.Upsert is { } requested
            ? requested.ExpectedRevision : original.Delete!.ExpectedRevision);
        if (original.Delete is not null && previous is null)
        {
            throw Errors.Fail(ErrorCode.NotFound, GraphEdgeUnavailable);
        }
        if (tx.VisitRange(GraphCrossPartitionKeys.IntentEdgePrefix(original.SourcePartition, graph, edgeId),
            RemoteGraphLegacyExistenceProbe, static (_, _) => false).Records != RemoteGraphAbsentRevision)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteGraphProtocol.Invalid);
        }
        foreach (var preparation in command.Preparations)
        {
            var claims = Verify<RemoteGraphPreparationClaimsV1>(preparation.Token);
            if (claims.ExpiresAt <= operation.EvaluatedAt)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteGraphProtocol.Invalid);
            }
        }
        return previous;
    }

    private const int RemoteGraphLegacyExistenceProbe = 1;
}
