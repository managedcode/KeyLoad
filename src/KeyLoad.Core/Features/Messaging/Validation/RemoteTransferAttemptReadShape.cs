namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferAttemptReadShape
{
    internal static void Require(RemoteTransferCoordinationReadRequest request)
    {
        if (request is null || request.Source?.Partition is null || request.Destination?.Partition is null
            || request.TransferId == Guid.Empty || request.ExpectedGeneration < RemoteTransferAttemptProtocol.FirstGeneration
            || request.AcceptCommandId == Guid.Empty || string.IsNullOrEmpty(request.IntentToken)
            || request.Purpose is not (RemoteTransferAttemptProtocol.SourceReadPurpose or RemoteTransferAttemptProtocol.FailureReadPurpose
                or RemoteTransferRepairProtocol.ReadPurpose))
        { throw Errors.Fail(ErrorCode.Validation, RemoteTransferAttemptProtocol.Invalid); }
        RequireRepairFields(request);
        DatabaseEngine.ValidatePartition(request.Source.Partition);
        DatabaseEngine.ValidatePartition(request.Destination.Partition);
        JsonData.Identifier(request.Source.Queue);
        JsonData.Identifier(request.Destination.Queue);
    }

    private static void RequireRepairFields(RemoteTransferCoordinationReadRequest request)
    {
        RemoteTransferRepairIdentity.RequireGenerations(request.ExpectedGeneration, request.PolicyGeneration, request.CompleteGeneration);
        if (request.Purpose == RemoteTransferRepairProtocol.ReadPurpose)
        {
            if (request.RepairStage is not { } stage || !Enum.IsDefined(stage)
                || (stage == QueueTransferRepairStage.Accept ? request.ReceiptToken is not null : string.IsNullOrEmpty(request.ReceiptToken)))
            { throw Errors.Fail(ErrorCode.Validation, RemoteTransferRepairProtocol.Invalid); }
            return;
        }
        if (request.RepairStage is not null || request.ReceiptToken is not null
            || request.Purpose == RemoteTransferAttemptProtocol.FailureReadPurpose
                && request.PolicyGeneration != RemoteTransferRepairProtocol.InitialGeneration)
        { throw Errors.Fail(ErrorCode.Validation, RemoteTransferRepairProtocol.Invalid); }
    }
}
