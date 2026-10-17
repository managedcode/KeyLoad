namespace KeyLoad.Core.Features.Messaging;

internal static class RemoteTransferAttemptProtocol
{
    internal const long FirstGeneration = 1;
    internal const int NoHistory = 0;
    internal const long MinimumNativePosition = 1;
    internal const long MinimumPolicyEpoch = 1;
    internal const long NoUsage = 0;
    internal const int DigestCharacters = 64;
    internal const int SingleMutation = 1;
    internal const int FirstIndex = 0;
    internal const int GuidBytes = 16;
    internal const string SourceReadPurpose = "keyload.queue-transfer.source-attempt-read.v1";
    internal const string FailureReadPurpose = "keyload.queue-transfer.accept-failure-read.v1";
    internal const string FailurePurpose = "keyload.queue-transfer.accept-failure.v1";
    internal const string AttemptDomain = "keyload.queue-transfer.accept-attempt-command.v1";
    internal const string AdvanceDomain = "keyload.queue-transfer.advance-attempt-command.v1";
    internal const string Invalid = "The native queue transfer attempt identity is invalid.";
    internal const string Stale = "The queue transfer attempt generation or dependency is stale.";
    internal const string Unavailable = "The native queue transfer attempt is unavailable.";
    internal const string Capacity = "The retained queue transfer attempt capacity is exhausted.";
    internal const string AdvanceKind = "queueTransferAttemptAdvanced";
}
