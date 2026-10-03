namespace KeyLoad.Core.Features.Messaging;

/// <summary>Holds a fenced lease and stored body size without retaining its payload.</summary>

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.ValidatedQueueLease)]
internal readonly record struct ValidatedQueueLease(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ValidatedQueueLeaseFields.Claims)] DeliveryClaims Claims,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ValidatedQueueLeaseFields.Metadata)] MessageMetadata Metadata,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ValidatedQueueLeaseFields.BodyBytes)] long BodyBytes);
