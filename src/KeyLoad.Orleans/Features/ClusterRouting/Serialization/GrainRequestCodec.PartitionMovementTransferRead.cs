namespace KeyLoad.Orleans;

public sealed partial class GrainRequestCodec
{
    internal string CreatePartitionMovementRetireCancellationOutcome(Guid requestId, string principalId,
        PartitionMovementRetireCancellationOutcomeQuery query, DateTimeOffset expiry)
        => CreateMovementTransferRead(requestId, principalId, GrainReadKind.PartitionMovementRetireCancellationOutcome,
            PartitionMovementRequestScope.RetireCancellationPurpose, NativeSerialization.Serialize(query), expiry);

    internal string CreatePartitionMovementReceiverIssuance(Guid requestId, string principalId,
        PartitionMovementReceiverIssuanceQuery query, DateTimeOffset expiry)
        => CreateMovementTransferRead(requestId, principalId, GrainReadKind.PartitionMovementReceiverIssuance,
            PartitionMovementRequestScope.ReceiverIssuancePurpose, NativeSerialization.Serialize(query), expiry);

    internal string CreatePartitionMovementParentState(Guid requestId, string principalId,
        PartitionMovementParentStateQuery query, DateTimeOffset expiry)
        => CreateMovementTransferRead(requestId, principalId, GrainReadKind.PartitionMovementParentState,
            PartitionMovementRequestScope.ParentStatePurpose, NativeSerialization.Serialize(query), expiry);

    internal string CreatePartitionMovementTransferAuthority(Guid requestId, string principalId,
        PartitionMovementTransferAuthorityQuery query, DateTimeOffset expiry)
        => CreateMovementTransferRead(requestId, principalId, GrainReadKind.PartitionMovementTransferAuthority,
            PartitionMovementRequestScope.TransferAuthorityPurpose, NativeSerialization.Serialize(query), expiry);

    internal string CreatePartitionMovementTransferData(Guid requestId, string principalId,
        PartitionMovementTransferDataCapability query, DateTimeOffset expiry)
        => CreateMovementTransferRead(requestId, principalId, GrainReadKind.PartitionMovementTransferData,
            PartitionMovementRequestScope.TransferDataPurpose, NativeSerialization.Serialize(query), expiry);

    private string CreateMovementTransferRead(Guid requestId, string principalId, GrainReadKind kind,
        string purpose, byte[] payload, DateTimeOffset expiry)
    {
        return Issue(GrainRequestEnvelopeConstruction.MovementRead(database, clock.GetUtcNow(),
            settings.RequestLifetime, requestId, principalId, kind, purpose, payload, expiry));
    }
}
