namespace KeyLoad.Core.Features.Messaging;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(RemoteTransferAliases.Capacity)]
internal sealed record RemoteTransferCapacity(
    [property: global::Orleans.Id(RemoteTransferFields.StoredRecords)] long StoredRecords,
    [property: global::Orleans.Id(RemoteTransferFields.StoredBytes)] long StoredBytes);
