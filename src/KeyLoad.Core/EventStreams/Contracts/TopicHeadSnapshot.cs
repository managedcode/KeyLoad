namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.TopicHeadSnapshot)]
internal readonly record struct TopicHeadSnapshot(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.TopicHeadSnapshotFields.Head)] EventSourceHead Head,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.TopicHeadSnapshotFields.StoredBytes)] long StoredBytes);
