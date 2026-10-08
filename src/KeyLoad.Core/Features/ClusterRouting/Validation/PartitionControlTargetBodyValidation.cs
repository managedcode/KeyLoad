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
        if (body.Delegation is null || body.Control is null || body.Command is null
            || body.OperatorPrincipalId != body.Control.PrincipalId
            || body.Control.MoveId != record.Delegation!.MoveId
            || JsonData.Fingerprint(body.Delegation) != JsonData.Fingerprint(record.Delegation)
            || JsonData.Fingerprint(body.Delegation.TargetPlacement) != JsonData.Fingerprint(record.Destination)
            || body.Command.CommandId != record.Identity.CommandId
            || body.Command.Partition != record.Identity.Partition
            || JsonData.Fingerprint(body.Command) != JsonData.Fingerprint(
                NativeCommandPayload.Read<CommandRequest>(record.OriginalOperation!)))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
    }
}
