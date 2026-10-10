namespace KeyLoad.Core.Features.Messaging;

internal static class TargetInboxProtocol
{
    internal const string RecordAlias = "keyload.core.target-inbox-record.v1";
    internal const string CapacityAlias = "keyload.core.target-inbox-capacity.v1";
    internal const string RecordSpace = "processing-inbox";
    internal const string CapacitySpace = "processing-inbox-capacity";
    internal const long EmptyCount = 0;
    internal const long MinimumCount = 1;
    internal const string Invalid = "The target inbox identity or policy is invalid.";
    internal const string Unavailable = "The target inbox is not configured.";
    internal const string Conflict = "The target inbox input was completed with different effects.";
    internal const string Corrupt = "The retained target inbox is inconsistent.";
    internal const string Exhausted = "The retained target inbox capacity is exhausted.";
}

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(TargetInboxProtocol.RecordAlias)]
internal sealed record TargetInboxRecord(
    [property: global::Orleans.Id(0)] string IdentityFingerprint,
    [property: global::Orleans.Id(1)] string EffectsFingerprint,
    [property: global::Orleans.Id(2)] CommitReceipt Receipt);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(TargetInboxProtocol.CapacityAlias)]
internal sealed record TargetInboxCapacity(
    [property: global::Orleans.Id(0)] long Count,
    [property: global::Orleans.Id(1)] long Bytes);
