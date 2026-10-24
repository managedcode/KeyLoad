namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.InboxRecord)]
internal sealed record InboxRecord(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.InboxRecordFields.Fingerprint)] string Fingerprint,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.InboxRecordFields.Receipt)] CommitReceipt Receipt);
