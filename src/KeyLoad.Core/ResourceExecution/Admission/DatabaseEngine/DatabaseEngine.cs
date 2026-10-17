using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void ObserveControlledResourceEffects(IKeyValueView view, ReplicatedOperation operation)
    {
        var authenticated = PartitionMovementNativePayload.ReadAuthenticated(operation);
        var phase = authenticated.Original;
        if (phase.Stage == PartitionMovePeerStage.ControlApplyCommand)
        {
            var proof = authenticated.LifetimeProofs.Single();
            var coverage = PartitionControlLifetimeProofValidation.Require(phase, proof, Limits.MaxBatchBytes);
            var body = NativeSerialization.Deserialize<PartitionControlApplyBody>(phase.Body.Span);
            var paired = new PartitionControlResourceView(view, phase.Partition, body.Delegation.Resources,
                coverage, Limits.MaxBatchBytes);
            ObserveOriginalControlledEffects(paired, body.Delegation.Principal, body.Delegation.OriginalOperation ?? throw Errors.Fail(ErrorCode.Corruption, ResourceEffectAdmissionProtocol.Invalid),
                body.Delegation.Resources);
            return;
        }
        if (phase.Stage == PartitionMovePeerStage.ControlAdmitCommand)
        {
            var body = NativeSerialization.Deserialize<PartitionControlAdmitBody>(phase.Body.Span);
            var original = VerifyOperationAuthority(body.OriginalOperation);
            var principal = Principal(view, original.PrincipalId, operation.EvaluatedAt);
            var definitions = PartitionMoveResources.Capture(view, phase.Partition, Limits);
            RequireControlledCommandPolicies(principal, original, phase.Partition, definitions);
            var coverage = PartitionControlLifetimeCoverage.Capture(view, phase.Partition, definitions, Limits.MaxBatchBytes);
            var paired = new PartitionControlResourceView(view, phase.Partition, definitions, coverage, Limits.MaxBatchBytes);
            ObserveOriginalControlledEffects(paired, principal, original, definitions);
        }
    }

    private void ObserveOriginalControlledEffects(IKeyValueView paired, PrincipalRecord principal,
        ReplicatedOperation original, System.Collections.Immutable.ImmutableArray<ResourceDefinition> definitions)
    {
        var originalAdmission = ResourceEffectAdmissionValidation.Read(original, Limits);
        if (originalAdmission.Error is { } denied && denied != ErrorCode.OwnershipLost)
        {
            throw Errors.Fail(denied, originalAdmission.SafeDetail!);
        }
        if (original.Kind == OperationKind.Batch)
        {
            AuthorizeBatchMutations(paired, principal, Payload<CommandRequest>(original), false,
                BatchResourceAdmission.RetiredOutcomeMetadata);
        }
        else
        {
            var scope = RequireControlledBlobPolicies(principal, original, definitions);
            _ = BindResourceDefinition(paired, scope.Blob.Partition, scope.Blob.Resource, ResourceKind.BlobStore);
        }
    }
}
