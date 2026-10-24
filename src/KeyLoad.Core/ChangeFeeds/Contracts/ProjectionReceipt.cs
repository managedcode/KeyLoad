namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.InternalSerialization.CoreAliases.ProjectionReceipt)]
internal sealed record ProjectionReceipt(
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ProjectionReceiptFields.Fingerprint)] string Fingerprint,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ProjectionReceiptFields.Receipt)] CommitReceipt Receipt,
    [property: global::Orleans.Id(global::KeyLoad.Core.InternalSerialization.ProjectionReceiptFields.Checkpoint)] long Checkpoint);
