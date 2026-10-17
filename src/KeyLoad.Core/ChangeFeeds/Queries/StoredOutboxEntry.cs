namespace KeyLoad.Core.Features.ChangeFeeds;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.StoredOutboxEntry)]
internal sealed record StoredOutboxEntry(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutboxEntryFields.Entry)] OutboxEntry Entry,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutboxEntryFields.Key)] byte[] Key,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.StoredOutboxEntryFields.StoredBytes)] int StoredBytes);
