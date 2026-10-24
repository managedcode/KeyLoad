namespace KeyLoad.Core.Messaging;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(TargetInboxProtocol.CapacityAlias)]
internal sealed record TargetInboxCapacity(
    [property: global::Orleans.Id(0)] long Count,
    [property: global::Orleans.Id(1)] long Bytes);
