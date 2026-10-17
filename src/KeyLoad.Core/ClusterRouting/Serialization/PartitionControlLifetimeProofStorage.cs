using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

internal static class PartitionControlLifetimeProofStorage
{
    private const string CommandSpace = "partition-control-lifetime-proof-v1";
    private const string GrantSpace = "partition-move-lifetime-proof-v1";

    private static byte[] CommandKey(PartitionControlCommandIdentity identity)
        => KeyCodec.Encode(CommandSpace, PartitionControlCommandKeys.Authority(identity));

    private static byte[] GrantKey(PartitionMovePhaseGrant grant)
        => KeySpace.Partition(GrantSpace, grant.Partition, grant.GrantId);

    internal static PartitionControlLifetimeProofV1 ReadCommand(IKeyValueView view,
        PartitionControlCommandRecord record, int maximumBytes)
    {
        var proof = PartitionMoveTargetStorage.Read<PartitionControlLifetimeProofV1>(view,
            CommandKey(record.Identity), maximumBytes)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        if (proof.OriginalDelegation is null || record.Delegation is null
            || record.Delegation.ControlAdmissionPosition != record.AdmissionPosition
            || record.Delegation.ControlOwner is null
            || !PhysicalOwnerEntryValidation.SameOwner(record.Delegation.ControlOwner, record.ControlOwner)
            || !PartitionControlLifetimeProofValidation.Same(proof.OriginalDelegation, record.Delegation))
        {
            throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid);
        }
        PartitionControlLifetimeCoverage.Require(proof.Coverage, record.Identity.Partition!,
            record.Delegation.Resources, maximumBytes);
        return proof;
    }

    internal static PartitionMoveLifetimeProofV1 ReadGrant(IKeyValueView view,
        PartitionMovePhaseGrant grant, int maximumBytes)
    {
        var proof = PartitionMoveTargetStorage.Read<PartitionMoveLifetimeProofV1>(view,
            GrantKey(grant), maximumBytes)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        if (proof.OriginalGrant is null || !PartitionControlLifetimeProofValidation.Same(
            proof.OriginalGrant, PartitionControlLifetimeProofValidation.Issued(grant)))
        {
            throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid);
        }
        return proof;
    }

    internal static void WriteCommand(IAtomicTransaction transaction, PartitionControlCommandRecord record,
        PartitionControlLifetimeProofV1 proof, int maximumBytes)
        => PartitionMoveTargetStorage.Write(transaction, CommandKey(record.Identity), proof, maximumBytes);

    internal static void WriteGrant(IAtomicTransaction transaction, PartitionMoveLifetimeProofV1 proof, int maximumBytes)
        => PartitionMoveTargetStorage.Write(transaction, GrantKey(proof.OriginalGrant), proof, maximumBytes);
}
