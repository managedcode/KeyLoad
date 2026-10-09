namespace KeyLoad.Orleans;

internal static class PartitionMovementRequestScope
{
    internal const string RetireCancellationPurpose = "keyload-partition-movement-retire-cancellation-outcome-query-v1";
    internal const string ReceiverIssuancePurpose = "keyload-partition-movement-receiver-issuance-query-v1";
    internal const string ParentStatePurpose = "keyload-partition-movement-parent-state-query-v1";
    internal const string TransferAuthorityPurpose = "keyload-partition-movement-transfer-authority-query-v1";
    internal const string TransferDataPurpose = "keyload-partition-movement-transfer-data-query-v1";
    internal const string OutcomePurpose = "keyload-partition-movement-outcome-query-v1";
    internal const string Purpose = "keyload-partition-movement-verified-v1";

    internal static bool Validate(GrainRequestEnvelope request)
    {
        if (request.ReadKind is GrainReadKind.PartitionMovementTransferAuthority or GrainReadKind.PartitionMovementTransferData or GrainReadKind.PartitionMovementParentState or GrainReadKind.PartitionMovementReceiverIssuance or GrainReadKind.PartitionMovementRetireCancellationOutcome
            || request.Purpose is TransferAuthorityPurpose or TransferDataPurpose or ParentStatePurpose or ReceiverIssuancePurpose or RetireCancellationPurpose)
        {
            var expected = request.ReadKind switch
            {
                GrainReadKind.PartitionMovementTransferAuthority => TransferAuthorityPurpose,
                GrainReadKind.PartitionMovementTransferData => TransferDataPurpose,
                GrainReadKind.PartitionMovementParentState => ParentStatePurpose,
                GrainReadKind.PartitionMovementReceiverIssuance => ReceiverIssuancePurpose,
                GrainReadKind.PartitionMovementRetireCancellationOutcome => RetireCancellationPurpose,
                _ => null
            };
            if (expected is null || request.Purpose != expected || request.CommandKind is not null || request.CommandId != Guid.Empty)
            { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
            return true;
        }
        var outcome = request.ReadKind == GrainReadKind.PartitionMovementOutcome;
        if (request.Purpose == OutcomePurpose || outcome)
        {
            if (!outcome || request.Purpose != OutcomePurpose || request.CommandKind is not null
                || request.CommandId != Guid.Empty)
            { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
            return true;
        }
        var movement = request.CommandKind == OperationKind.PartitionMovementPhase;
        var capture = request.ReadKind == GrainReadKind.PartitionMovementCapture;
        if (request.Purpose != Purpose)
        {
            if (movement || capture)
            { throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired); }
            return false;
        }
        if (!(movement && request.ReadKind is null
            || capture && request.CommandKind is null))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        return true;
    }
}
