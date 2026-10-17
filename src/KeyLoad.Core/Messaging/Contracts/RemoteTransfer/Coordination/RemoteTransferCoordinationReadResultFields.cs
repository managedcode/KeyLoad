namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferCoordinationReadResultFields
{
    internal const uint SourceState = 0;
    internal const uint FailureWitness = 1;
    internal const uint TargetReceipt = 2;
    internal const uint RepairWitness = 3;
    internal const string Alias = "keyload.core.queue-transfer.coordination-read-result.v1";
}
