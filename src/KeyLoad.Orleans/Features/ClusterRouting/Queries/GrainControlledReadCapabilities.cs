using KeyLoad.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal static class GrainControlledReadCapabilities
{
    internal static bool Handles(GrainReadKind kind)
        => kind is GrainReadKind.ControlledBlob or GrainReadKind.ControlledDocument or GrainReadKind.PartitionMovementOutcome or GrainReadKind.PartitionMovementTransferAuthority or GrainReadKind.PartitionMovementParentState or GrainReadKind.PartitionMovementReceiverIssuance or GrainReadKind.PartitionMovementRetireCancellationOutcome;

    internal static object Execute(DatabaseEngine database, IServiceProvider services,
        TimeProvider clock, PrincipalRecord principal, DecodedGrainRequest request,
        CancellationToken cancellationToken)
    {
        var limits = services.GetRequiredService<IOptions<DatabaseLimits>>();
        return request.Envelope.ReadKind switch
        {
            GrainReadKind.ControlledBlob => ControlledBlobReadExecution.Execute(database,
                limits, clock, principal, request.Envelope,
                GrainNativePayload.Read<ControlledBlobReadRequest>(request.Payload), cancellationToken),
            GrainReadKind.ControlledDocument => ControlledDocumentReadExecution.Execute(database,
                limits, clock, principal, request.Envelope,
                GrainNativePayload.Read<ControlledDocumentReadRequest>(request.Payload), cancellationToken),
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
