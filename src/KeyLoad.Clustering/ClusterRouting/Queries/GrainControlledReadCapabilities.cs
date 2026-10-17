using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class GrainControlledReadCapabilities
{
    internal static bool Handles(GrainReadKind kind)
        => kind is GrainReadKind.ControlledBlobOutcomeV2 or GrainReadKind.OwnerCapacity or GrainReadKind.OwnedDocumentV2 or GrainReadKind.ControlledDocumentV2 or GrainReadKind.PartitionQueryLeafV2 or GrainReadKind.DistributedSearchLeafV2 or GrainReadKind.ControlledBlobV2 or GrainReadKind.PartitionMovementOutcome or GrainReadKind.PartitionMovementTransferAuthority or GrainReadKind.PartitionMovementParentState or GrainReadKind.PartitionMovementReceiverIssuance or GrainReadKind.PartitionMovementRetireCancellationOutcome;

    internal static object Execute(DatabaseEngine database, IServiceProvider services,
        TimeProvider clock, PrincipalRecord principal, DecodedGrainRequest request,
        IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        var limits = services.GetRequiredService<IOptions<DatabaseLimits>>();
        return request.Envelope.ReadKind switch
        {
            GrainReadKind.OwnerCapacity => OwnerCapacityReadExecution.Execute(database, principal,
                GrainNativePayload.Read<global::KeyLoad.Core.Features.ClusterRouting.Contracts.OwnerCapacityReadRequestV1>(request.Payload),
                services.GetRequiredService<ReplicaSiloDiscoveryState>(), limits, clock, cancellationToken),
            GrainReadKind.OwnedDocumentV2 or GrainReadKind.ControlledDocumentV2 => DocumentLifetimeReadCapabilities.Execute(
                database, services, clock, principal, request, binding, cancellationToken),
            GrainReadKind.PartitionQueryLeafV2 or GrainReadKind.DistributedSearchLeafV2 => QueryLifetimeReadCapability.Execute(
                database, services, clock, principal, request, binding, cancellationToken),
            GrainReadKind.ControlledBlobOutcomeV2 => ControlledBlobOutcomeReadExecutionV2.Execute(database,
                limits, clock, principal, request.Envelope,
                GrainNativePayload.Read<ControlledBlobReadRequestV2>(request.Payload), binding, cancellationToken),
            GrainReadKind.ControlledBlobV2 => ControlledBlobReadExecutionV2.Execute(database,
                limits, clock, principal, request.Envelope,
                GrainNativePayload.Read<ControlledBlobReadRequestV2>(request.Payload), binding, cancellationToken),
            GrainReadKind.PartitionMovementOutcome => PartitionMovementOutcomeExecution.Execute(database,
                principal, GrainNativePayload.Read<PartitionMovementOutcomeQuery>(request.Payload),
                limits, clock, cancellationToken),
            GrainReadKind.PartitionMovementRetireCancellationOutcome => PartitionMovementRetireCancellationOutcomeExecution.Execute(database,
                principal, GrainNativePayload.Read<PartitionMovementRetireCancellationOutcomeQuery>(request.Payload),
                limits, clock, request.Envelope.ExpiresAt, cancellationToken),
            GrainReadKind.PartitionMovementReceiverIssuance => PartitionMovementReceiverIssuanceExecution.Execute(database,
                principal, GrainNativePayload.Read<PartitionMovementReceiverIssuanceQuery>(request.Payload),
                limits, clock, request.Envelope.ExpiresAt, cancellationToken),
            GrainReadKind.PartitionMovementParentState => PartitionMovementParentStateExecution.Execute(database,
                principal, GrainNativePayload.Read<PartitionMovementParentStateQuery>(request.Payload),
                limits, clock, cancellationToken),
            GrainReadKind.PartitionMovementTransferAuthority => PartitionMovementTransferAuthorityExecution.Execute(database,
                principal, GrainNativePayload.Read<PartitionMovementTransferAuthorityQuery>(request.Payload),
                limits, clock, cancellationToken),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }
}
