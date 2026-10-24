namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.TopicHead)]
internal sealed record TopicHead(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.TopicHeadFields.TailPosition)] long TailPosition,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.TopicHeadFields.FirstAvailablePosition)] long FirstAvailablePosition,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.TopicHeadFields.Generation)] long Generation,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.TopicHeadFields.StoredBytes)] long StoredBytes);
