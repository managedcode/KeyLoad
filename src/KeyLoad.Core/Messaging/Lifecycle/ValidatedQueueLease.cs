namespace KeyLoad.Core.Messaging;

/// <summary>Holds a fenced lease and stored body size without retaining its payload.</summary>

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.ValidatedQueueLease)]
internal readonly record struct ValidatedQueueLease(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ValidatedQueueLeaseFields.Claims)] DeliveryClaims Claims,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ValidatedQueueLeaseFields.Metadata)] MessageMetadata Metadata,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ValidatedQueueLeaseFields.BodyBytes)] long BodyBytes);
