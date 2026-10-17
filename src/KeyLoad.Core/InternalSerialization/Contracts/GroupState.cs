namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.GroupState)]
internal sealed record GroupState(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupStateFields.Definition)] SubscriptionDefinition Definition,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupStateFields.Generation)] long Generation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupStateFields.OwnershipEpoch)] long OwnershipEpoch,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupStateFields.Checkpoint)] long Checkpoint,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupStateFields.IssuedPosition)] long IssuedPosition,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupStateFields.Paused)] bool Paused = false,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.GroupStateFields.SafeFailureCode)] string? SafeFailureCode = null);
