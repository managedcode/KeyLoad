namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.TopicPublicationProgress)]
internal readonly record struct TopicPublicationProgress(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.TopicPublicationProgressFields.Tail)] long Tail,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.TopicPublicationProgressFields.Sequence)] long Sequence,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.TopicPublicationProgressFields.StoredBytes)] long StoredBytes);
