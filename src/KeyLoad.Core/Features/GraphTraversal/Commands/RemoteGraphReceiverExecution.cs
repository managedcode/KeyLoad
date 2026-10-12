using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Models;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Core.Features.GraphTraversal.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult ExecuteRemoteGraphReceive(IAtomicTransaction tx,
        ReplicatedOperation operation, long position)
    {
        var command = Payload<RemoteGraphReceiverMutationV1>(operation);
        var claims = Verify<RemoteGraphReceiverClaimsV1>(command.VerifiedToken);
        if (claims.ExpiresAt <= operation.EvaluatedAt)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteGraphProtocol.Invalid); }
        var intent = command.SourceProof.Intent;
        var content = intent.Content;
        var key = RemoteGraphKeys.Receiver(content.Destination, content.Graph, content.SourcePartition, content.EdgeId);
        var previous = GraphCrossPartitionRecords.Read<RemoteGraphReceiverStateV1>(tx, key);
        if (previous is not null)
        {
            if (previous.Version != RemoteGraphProtocol.Version || previous.Content is null
                || previous.Intent?.Content is null || previous.Receipt is null)
            { throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid); }
            RequireRemoteGraphReceiptShape(previous.Receipt, ErrorCode.Corruption);
            if (!NativeSerialization.Serialize(previous.Intent).AsSpan().SequenceEqual(
                    NativeSerialization.Serialize(previous.Receipt.Intent))
                || previous.Content.Fingerprint != previous.Intent.Content.Fingerprint
                || previous.Content.Revision != previous.Intent.Content.Revision
                || previous.Content.Deleted != previous.Intent.Content.Deleted)
            { throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid); }
            GraphCrossPartitionValidation.ValidateReceiver(previous.Content, content.SourcePartition,
                content.Graph, content.EdgeId, content.Destination);
            if (previous.Content.Revision > content.Revision)
            { throw Errors.Fail(ErrorCode.RevisionConflict, RemoteGraphProtocol.Invalid); }
            if (previous.Content.Revision == content.Revision)
            {
                if (previous.Content.Fingerprint != content.Fingerprint
                    || previous.Receipt.TargetReceipt.CommandId != operation.Id)
                { throw Errors.Fail(ErrorCode.Conflict, RemoteGraphProtocol.Invalid); }
                var retained = new RemoteGraphReceiverResultV1(previous.Receipt, command);
                if (NativeSerialization.Measure(retained) > Limits.MaxBatchBytes)
                { throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteGraphProtocol.Invalid); }
                return Result(retained);
            }
        }
        var token = Token(tx, content.Destination.Partition, position);
        var mutation = new MutationReceipt(MutationDiscriminatorNames.ApplyCrossPartitionReverseEdge,
            content.Graph, content.EdgeId, content.Revision);
        var receipt = new RemoteGraphReceiptV1(RemoteGraphProtocol.Version, intent,
            new CommitReceipt(operation.Id, token, [mutation], Store.Identity.Durability));
        var result = new RemoteGraphReceiverResultV1(receipt, command);
        if (NativeSerialization.Measure(result) > Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteGraphProtocol.Invalid); }
        var state = new RemoteGraphReceiverStateV1(RemoteGraphProtocol.Version,
            new GraphCrossPartitionReceiverStateV1(GraphCrossPartitionProtocol.CurrentVersion, content.SourcePartition,
                content.Graph, content.EdgeId, content.Destination, content.Revision, content.Deleted, content.Fingerprint),
            intent, receipt);
        var bytes = GraphCrossPartitionRecords.Encode(state, Limits.MaxBatchBytes);
        RemoteGraphCapacityWriter.Apply(tx, content.Destination.Partition, RemoteGraphReceiverCapacity,
            KeySpace.Partition(RemoteGraphProtocol.ReceiverFamily, content.Destination.Partition),
            [new GraphCrossPartitionRecordWrite(key, bytes)], Limits);
        return Result(result);
    }

    private const string RemoteGraphReceiverCapacity = "receiver-states";
}
