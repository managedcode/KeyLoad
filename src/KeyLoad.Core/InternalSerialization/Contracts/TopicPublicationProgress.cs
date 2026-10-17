namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.TopicPublicationProgress)]
internal readonly record struct TopicPublicationProgress(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.TopicPublicationProgressFields.Tail)] long Tail,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.TopicPublicationProgressFields.Sequence)] long Sequence,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.TopicPublicationProgressFields.StoredBytes)] long StoredBytes);
