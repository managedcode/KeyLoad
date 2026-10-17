namespace KeyLoad.Core.Features.Messaging;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(RemoteTransferRepairStateFields.Alias)]
internal sealed record RemoteTransferRepairState(
    [property: global::Orleans.Id(RemoteTransferRepairStateFields.Ceiling)] int Ceiling,
    [property: global::Orleans.Id(RemoteTransferRepairStateFields.AcceptPolicyGeneration)] long AcceptPolicyGeneration,
    [property: global::Orleans.Id(RemoteTransferRepairStateFields.CompleteGeneration)] long CompleteGeneration,
    [property: global::Orleans.Id(RemoteTransferRepairStateFields.History)] System.Collections.Immutable.ImmutableArray<RemoteTransferRepairReference> History);
