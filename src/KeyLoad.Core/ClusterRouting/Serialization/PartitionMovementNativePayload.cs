using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

/// <summary>Reads only the mandatory authenticated native movement root.</summary>
internal static class PartitionMovementNativePayload
{
    private const int ControlledLifetimeProofCount = 1;
    private const int ControlledLifetimeProofIndex = 0;

    internal static PartitionMovePhaseCommand Read(ReplicatedOperation operation)
        => ReadAuthenticated(operation).Original;

    internal static PartitionMovementAuthenticatedCommandV1 ReadAuthenticated(ReplicatedOperation operation)
    {
        if (operation.Kind != OperationKind.PartitionMovementPhase)
        {
            throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid);
        }
        return Require(NativeCommandPayload.Read<PartitionMovementAuthenticatedCommandV1>(operation));
    }

    internal static PartitionMovementAuthenticatedCommandV1 ReadValue(ReadOnlyMemory<byte> value)
        => Require(NativeSerialization.Deserialize<PartitionMovementAuthenticatedCommandV1>(value.Span,
            NativeValidationProfile.PublicInputElements));

    internal static PartitionMovementAuthenticatedCommandV1 Require(PartitionMovementAuthenticatedCommandV1 value)
    {
        if (value is null || value.Original is null || value.LifetimeProofs.IsDefault
            || (value.Original.Stage == PartitionMovePeerStage.ControlApplyCommand
                ? value.LifetimeProofs.Length != ControlledLifetimeProofCount
                    || value.LifetimeProofs[ControlledLifetimeProofIndex] is null
                : !value.LifetimeProofs.IsEmpty))
        {
            throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        }
        return value;
    }
}
