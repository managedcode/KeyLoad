namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferCoordinationReadRequestFields
{
    internal const uint Purpose = 0;
    internal const uint Source = 1;
    internal const uint Destination = 2;
    internal const uint TransferId = 3;
    internal const uint IntentToken = 4;
    internal const uint ExpectedGeneration = 5;
    internal const uint AcceptCommandId = 6;
    internal const uint RepairStage = 7;
    internal const uint PolicyGeneration = 8;
    internal const uint CompleteGeneration = 9;
    internal const uint ReceiptToken = 10;
    internal const string Alias = "keyload.core.queue-transfer.coordination-read";
}
