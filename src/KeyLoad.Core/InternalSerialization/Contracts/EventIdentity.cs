namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.EventIdentity)]
internal sealed record EventIdentity(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.EventIdentityFields.StreamId)] string StreamId,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.EventIdentityFields.Generation)] long Generation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.EventIdentityFields.Revision)] long Revision);
