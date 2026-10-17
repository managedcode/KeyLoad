namespace KeyLoad.Core;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(global::KeyLoad.Core.Features.InternalSerialization.CoreNativeAliases.ProjectionReceipt)]
internal sealed record ProjectionReceipt(
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionReceiptFields.Fingerprint)] string Fingerprint,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionReceiptFields.Receipt)] CommitReceipt Receipt,
    [property: global::Orleans.Id(global::KeyLoad.Core.Features.InternalSerialization.ProjectionReceiptFields.Checkpoint)] long Checkpoint);
