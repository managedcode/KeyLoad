namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.GroupDelivery)]
internal sealed record GroupDelivery(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupDeliveryFields.Position)] long Position,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupDeliveryFields.State)] GroupDeliveryState State,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupDeliveryFields.Attempts)] int Attempts,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupDeliveryFields.LeaseVersion)] long LeaseVersion,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupDeliveryFields.AvailableAt)] DateTimeOffset? AvailableAt = null,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupDeliveryFields.LeaseUntil)] DateTimeOffset? LeaseUntil = null,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupDeliveryFields.PrincipalId)] string? PrincipalId = null);
