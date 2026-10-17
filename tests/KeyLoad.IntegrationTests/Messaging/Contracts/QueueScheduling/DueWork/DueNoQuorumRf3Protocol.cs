namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class DueNoQuorumRf3Protocol
{
    internal const string Database = "due-no-quorum";
    internal const string Domain = "due-no-quorum";
    internal const string Queue = "recurrences";
    internal const string InspectScheduleTool = "keyload_schedule_inspect";
    internal const string InspectMessageTool = "keyload_messages_inspect";
    internal const string PrincipalPrefix = "due-no-quorum-principal-";
    internal const string ApiKeyPrefix = "due-no-quorum-key-";
    internal const string SecretSeparator = ".";
    internal const string FieldReadGrant = "due.no-quorum.read";
    internal const string FieldUseGrant = "due.no-quorum.use";
    internal const string FieldWriteGrant = "due.no-quorum.write";
    internal const string Payload = "{\"secret\":\"due-no-quorum-payload\"}";
    internal const string Headers = "{\"secret\":\"due-no-quorum-headers\"}";
    internal const string Classification = "due-no-quorum-private";
    internal const string Utc = "UTC";
    internal const string SetupFailure = "The recurrence did not retain the required pre-fault setup lead.";
    internal const string LeaderFailure = "The actual three-node statuses did not identify one leader.";
    internal const string VoterFailure = "The fault plan did not retain exactly one original voter.";
    internal const int DueDelaySeconds = 90;
    internal const int MinimumSetupLeadSeconds = 30;
    internal const int NoQuorumObservationSeconds = 5;
    internal const int PollMilliseconds = 250;
    internal const int RandomSecretBytes = 32;
    internal const int OccurrenceLimit = 1;
    internal static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    internal static readonly TimeSpan ProgressDeadline = TimeSpan.FromMinutes(4);
    internal static readonly TimeSpan ParentDeadline = TimeSpan.FromMinutes(12);
}
