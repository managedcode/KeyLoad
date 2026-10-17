using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Models;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult ExecuteRemoteGraphComplete(IAtomicTransaction tx,
        ReplicatedOperation operation, long position)
    {
        var command = Payload<RemoteGraphCompletionMutationV1>(operation);
        var claims = Verify<RemoteGraphCompletionClaimsV1>(command.VerifiedToken);
        if (claims.ExpiresAt <= operation.EvaluatedAt)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteGraphProtocol.Invalid);
        }
        var intent = command.Receipt.Intent;
        var content = intent.Content;
        var key = RemoteGraphKeys.Intent(content.SourcePartition, content.Graph, content.EdgeId, content.Destination);
        var pending = GraphCrossPartitionRecords.Read<RemoteGraphIntentV1>(tx, key)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteGraphProtocol.Invalid);
        if (!NativeSerialization.Serialize(pending).AsSpan().SequenceEqual(NativeSerialization.Serialize(intent)))
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RemoteGraphProtocol.Invalid);
        }
        var token = Token(tx, content.SourcePartition, position);
        var mutation = new MutationReceipt(MutationDiscriminatorNames.CompleteCrossPartitionReverseEdge,
            content.Graph, content.EdgeId, content.Revision);
        var result = new RemoteGraphCompletionResultV1(
            new CommitReceipt(operation.Id, token, [mutation], Store.Identity.Durability), command);
        if (NativeSerialization.Measure(result) > Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteGraphProtocol.Invalid);
        }
        var completionKey = RemoteGraphKeys.Completion(content.SourcePartition, content.Graph, content.EdgeId,
            content.Destination, content.Fingerprint);
        RemoteGraphCapacityWriter.Apply(tx, content.SourcePartition, RemoteGraphCompletionCapacity,
            KeySpace.Partition(RemoteGraphProtocol.CompletionFamily, content.SourcePartition),
            [new GraphCrossPartitionRecordWrite(completionKey, GraphCrossPartitionRecords.Encode(result, Limits.MaxBatchBytes))], Limits);
        RemoteGraphCapacityWriter.Apply(tx, content.SourcePartition, RemoteGraphPendingCapacity,
            KeySpace.Partition(RemoteGraphProtocol.IntentFamily, content.SourcePartition),
            [new GraphCrossPartitionRecordWrite(key, null)], Limits);
        return Result(result);
    }

    private const string RemoteGraphCompletionCapacity = "completed-intents";
}
