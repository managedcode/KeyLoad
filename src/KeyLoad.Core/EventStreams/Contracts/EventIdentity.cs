namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.EventIdentity)]
internal sealed record EventIdentity(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.EventIdentityFields.StreamId)] string StreamId,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.EventIdentityFields.Generation)] long Generation,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.EventIdentityFields.Revision)] long Revision);
