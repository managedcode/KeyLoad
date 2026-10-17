namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.TopicHeadSnapshot)]
internal readonly record struct TopicHeadSnapshot(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.TopicHeadSnapshotFields.Head)] EventSourceHead Head,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.TopicHeadSnapshotFields.StoredBytes)] long StoredBytes);
