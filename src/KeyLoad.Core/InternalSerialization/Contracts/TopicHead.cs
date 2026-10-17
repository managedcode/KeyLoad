namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.TopicHead)]
internal sealed record TopicHead(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.TopicHeadFields.TailPosition)] long TailPosition,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.TopicHeadFields.FirstAvailablePosition)] long FirstAvailablePosition,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.TopicHeadFields.Generation)] long Generation,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.TopicHeadFields.StoredBytes)] long StoredBytes);
