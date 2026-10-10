namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferRepairStateFields
{
    internal const uint Ceiling = 0;
    internal const uint AcceptPolicyGeneration = 1;
    internal const uint CompleteGeneration = 2;
    internal const uint History = 3;
    internal const string Alias = "keyload.core.queue-transfer.repair-state.v1";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferRepairStateFields.Alias)]
internal sealed record RemoteTransferRepairState(
    [property: global::Orleans.Id(RemoteTransferRepairStateFields.Ceiling)] int Ceiling,
    [property: global::Orleans.Id(RemoteTransferRepairStateFields.AcceptPolicyGeneration)] long AcceptPolicyGeneration,
    [property: global::Orleans.Id(RemoteTransferRepairStateFields.CompleteGeneration)] long CompleteGeneration,
    [property: global::Orleans.Id(RemoteTransferRepairStateFields.History)] System.Collections.Immutable.ImmutableArray<RemoteTransferRepairReference> History);
