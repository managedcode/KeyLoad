namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.GroupDelivery)]
internal sealed record GroupDelivery(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupDeliveryFields.Position)] long Position,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupDeliveryFields.State)] GroupDeliveryState State,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupDeliveryFields.Attempts)] int Attempts,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupDeliveryFields.LeaseVersion)] long LeaseVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupDeliveryFields.AvailableAt)] DateTimeOffset? AvailableAt = null,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupDeliveryFields.LeaseUntil)] DateTimeOffset? LeaseUntil = null,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupDeliveryFields.PrincipalId)] string? PrincipalId = null);
