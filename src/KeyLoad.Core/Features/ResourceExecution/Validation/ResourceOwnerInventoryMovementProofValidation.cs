using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceOwnerInventoryMovementProofValidation
{
    // The caller already ran the original full installed-image validation. Its descriptor family
    // digest therefore proves the exact actual native records; do not rescan or invent a second image.
    internal static void Require(ResourceOwnerInventoryMovementProofV1 proof, PartitionMoveTargetStage stage,
        DatabaseLimits limits)
    {
        if (proof.Version != ResourceOwnerInventoryMovementProtocol.Version || proof.Fence is null
            || proof.MemberCount < ResourceOwnerInventoryMovementProtocol.First
            || proof.MemberDigest is null || NativeSerialization.Measure(proof) > limits.MaxBatchBytes
            || !NativeSerialization.Serialize(proof.Fence).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(stage.Fence)))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryMovementProtocol.Invalid);
        }
        var members = stage.Descriptor.Families.SingleOrDefault(
            family => family.Family == PartitionRecordFamilies.ResourceOwnerInventoryMember);
        if (members is null || members.RecordCount != proof.MemberCount || members.Digest != proof.MemberDigest)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryMovementProtocol.Invalid);
        }
        if (proof.Origin is { } origin && (origin.Version != ResourceOwnerInventoryMovementProtocol.Version
            || origin.Revision < ResourceOwnerInventoryMovementProtocol.Step
            || origin.Proven && origin.StoreIncarnation != stage.Fence.SourcePlacement.Incarnation))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryMovementProtocol.Invalid);
        }
    }
}
