namespace KeyLoad.Core.Features.Messaging;

internal static class TargetInboxProtocol
{
    internal const string RecordAlias = "keyload.core.target-inbox-record";
    internal const string CapacityAlias = "keyload.core.target-inbox-capacity";
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
