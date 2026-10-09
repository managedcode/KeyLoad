using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionControlTargetBodyValidation
{
    internal static void Require(PartitionControlCommandRecord record)
    {
        if (record.TargetBody.IsEmpty)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        var body = NativeSerialization.Deserialize<PartitionControlApplyBody>(record.TargetBody.Span);
        if (body.Delegation is null || body.Control is null
            || body.OperatorPrincipalId != body.Control.PrincipalId
            || body.Control.MoveId != record.Delegation!.MoveId
            || JsonData.Fingerprint(body.Delegation) != JsonData.Fingerprint(record.Delegation)
            || JsonData.Fingerprint(body.Delegation.TargetPlacement) != JsonData.Fingerprint(record.Destination)
            || !SameOriginalBody(record, body))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
    }
    private static bool SameOriginalBody(PartitionControlCommandRecord record, PartitionControlApplyBody body)
    {
        var original = record.OriginalOperation!;
        if (original.Kind == OperationKind.Batch)
        {
            return body.Command is { } command && command.CommandId == record.Identity.CommandId
                && command.Partition == record.Identity.Partition
                && JsonData.Fingerprint(command) == JsonData.Fingerprint(NativeCommandPayload.Read<CommandRequest>(original));
        }
        if (body.Command is not null || !BlobStorageOperations.Handles(original.Kind))
        { return false; }
        var blob = BlobCommandScope.From(original);
        BlobKeys.Validate(blob.Blob);
        return blob.CommandId == record.Identity.CommandId && blob.Blob.Partition == record.Identity.Partition;
    }
}
