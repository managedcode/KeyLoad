namespace KeyLoad.Core.Messaging;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(TargetInboxProtocol.RecordAlias)]
internal sealed record TargetInboxRecord(
    [property: global::Orleans.Id(0)] string IdentityFingerprint,
    [property: global::Orleans.Id(1)] string EffectsFingerprint,
    [property: global::Orleans.Id(2)] CommitReceipt Receipt);
