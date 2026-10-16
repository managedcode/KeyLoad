using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireRetainedControlledGrantLifetimeProof(IKeyValueView view, ReplicatedOperation original,
        PartitionMovePhaseCommand effect, PartitionMovePhaseGrant grant)
    {
        var retained = PartitionControlLifetimeProofStorage.ReadGrant(view, grant, Limits.MaxBatchBytes);
        if (retained.AuthorizationFingerprint != NativeOperationFingerprint.Compute(original))
        {
            throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        }
        _ = PartitionControlLifetimeProofValidation.Require(effect with
        {
            GrantId = grant.GrantId,
            Resources = grant.Resources
        }, retained, Limits.MaxBatchBytes);
    }

    private void CreateControlledGrantLifetimeProof(IAtomicTransaction transaction, ReplicatedOperation original,
        PartitionMovePhaseCommand effect, PartitionMovePhaseGrant grant)
    {
        var body = NativeSerialization.Deserialize<PartitionControlApplyBody>(effect.Body.Span);
        var record = PartitionControlCommandStorage.Read(transaction, body.Delegation.Identity, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        var controlProof = PartitionControlLifetimeProofStorage.ReadCommand(transaction, record, Limits.MaxBatchBytes);
        var proof = new PartitionMoveLifetimeProofV1(grant, NativeOperationFingerprint.Compute(original), controlProof);
        var admitted = effect with
        {
            GrantId = grant.GrantId,
            Resources = grant.Resources
        };
        _ = PartitionControlLifetimeProofValidation.Require(admitted, proof, Limits.MaxBatchBytes);
        PartitionControlLifetimeCoverage.RequireCurrent(transaction, controlProof.Coverage, grant.Resources, Limits.MaxBatchBytes);
        PartitionControlLifetimeProofStorage.WriteGrant(transaction, proof, Limits.MaxBatchBytes);
    }
}
