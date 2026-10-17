namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.RetainedTopicEventIdentity)]
internal sealed record RetainedTopicEventIdentity([property: Orleans.Id(0)] string ContentDigest,
    [property: Orleans.Id(1)] long Position, [property: Orleans.Id(2)] long Generation);
