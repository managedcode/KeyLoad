namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.ReadyClaimInput)]
internal sealed record ReadyClaimInput(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ReadyClaimInputFields.ReadyKey)] byte[] ReadyKey,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ReadyClaimInputFields.MetadataKey)] byte[] MetadataKey,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ReadyClaimInputFields.Metadata)] MessageMetadata Metadata,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ReadyClaimInputFields.TransitionKey)] byte[] TransitionKey,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ReadyClaimInputFields.DeletesBody)] bool DeletesBody,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ReadyClaimInputFields.StrictOrderKey)] byte[]? StrictOrderKey = null);
