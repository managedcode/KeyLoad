namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.GroupState)]
internal sealed record GroupState(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupStateFields.Definition)] SubscriptionDefinition Definition,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupStateFields.Generation)] long Generation,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupStateFields.OwnershipEpoch)] long OwnershipEpoch,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupStateFields.Checkpoint)] long Checkpoint,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupStateFields.IssuedPosition)] long IssuedPosition,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupStateFields.Paused)] bool Paused = false,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.GroupStateFields.SafeFailureCode)] string? SafeFailureCode = null);
