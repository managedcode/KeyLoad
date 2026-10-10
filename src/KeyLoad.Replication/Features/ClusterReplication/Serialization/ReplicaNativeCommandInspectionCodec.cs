using KeyLoad.Core.Features.InternalSerialization;
using Orleans.Serialization.Buffers;

namespace KeyLoad.Replication;

// Read-only admission profile; normal command writers remain the official generated codec.
internal sealed class ReplicaNativeCommandInspectionCodec : ReplicaRecordInspectionCodec<NativeCommandPayload>
{
    protected override NativeCommandPayload ReadFields<TInput>(ref Reader<TInput> reader)
    {
        var result = new NativeCommandPayload(
            ReplicaInspectionFields.Read<ReadOnlyMemory<byte>, TInput>(ref reader, NativeCommandContract.ValueId),
            ReplicaInspectionFields.Read<ErrorCode?, TInput>(ref reader, NativeCommandContract.ErrorId - NativeCommandContract.ValueId),
            ReplicaInspectionFields.Read<string?, TInput>(ref reader, NativeCommandContract.SafeDetailId - NativeCommandContract.ErrorId));
        return result with
        {
            Authority = ReplicaInspectionFields.Read<ReadOnlyMemory<byte>, TInput>(ref reader,
                NativeCommandContract.AuthorityId - NativeCommandContract.SafeDetailId),
            Signature = ReplicaInspectionFields.Read<ReadOnlyMemory<byte>, TInput>(ref reader,
                NativeCommandContract.SignatureId - NativeCommandContract.AuthorityId),
            RetryDecisions = ReplicaInspectionFields.Read<ReadOnlyMemory<byte>, TInput>(ref reader,
                NativeCommandContract.RetryDecisionsId - NativeCommandContract.SignatureId),
            TransferProof = ReplicaInspectionFields.Read<ReadOnlyMemory<byte>, TInput>(ref reader,
                NativeCommandContract.TransferProofId - NativeCommandContract.RetryDecisionsId)
        };
    }

    protected override void WriteFields<TBufferWriter>(ref Writer<TBufferWriter> writer, NativeCommandPayload value)
    {
        ReplicaInspectionFields.Write(ref writer, NativeCommandContract.ValueId, value.Value);
        ReplicaInspectionFields.Write(ref writer, NativeCommandContract.ErrorId - NativeCommandContract.ValueId, value.Error);
        ReplicaInspectionFields.Write(ref writer, NativeCommandContract.SafeDetailId - NativeCommandContract.ErrorId, value.SafeDetail);
        ReplicaInspectionFields.Write(ref writer, NativeCommandContract.AuthorityId - NativeCommandContract.SafeDetailId, value.Authority);
        ReplicaInspectionFields.Write(ref writer, NativeCommandContract.SignatureId - NativeCommandContract.AuthorityId, value.Signature);
        ReplicaInspectionFields.Write(ref writer, NativeCommandContract.RetryDecisionsId - NativeCommandContract.SignatureId, value.RetryDecisions);
        ReplicaInspectionFields.Write(ref writer, NativeCommandContract.TransferProofId - NativeCommandContract.RetryDecisionsId, value.TransferProof);
    }
}
