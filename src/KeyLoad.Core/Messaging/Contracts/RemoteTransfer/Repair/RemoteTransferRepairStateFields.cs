namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferRepairStateFields
{
    internal const uint Ceiling = 0;
    internal const uint AcceptPolicyGeneration = 1;
    internal const uint CompleteGeneration = 2;
    internal const uint History = 3;
    internal const string Alias = "keyload.core.queue-transfer.repair-state.v1";
}
