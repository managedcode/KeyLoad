namespace KeyLoad.Core.ChangeFeeds;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.StoredOutboxEntry)]
internal sealed record StoredOutboxEntry(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.StoredOutboxEntryFields.Entry)] OutboxEntry Entry,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.StoredOutboxEntryFields.Key)] byte[] Key,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.StoredOutboxEntryFields.StoredBytes)] int StoredBytes);
