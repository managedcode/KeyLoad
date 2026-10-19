namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class EventMessageSensitiveSafeR52Protocol
{
    internal const string Resource = "safe-sensitive-input";
    internal const string First = "safe-first";
    internal const string Second = "safe-second";
    internal const string EventType = "SafeSensitiveInput";
    internal const string Canary = "SAFE_ABSENT_CLUSTER_LITERAL_CANARY";
    internal const string MissingPath = "/absent-sensitive";
    internal const string Payload = "{\"public\":{\"items\":[\"Київ\",null,2]},\"note\":\"SAFE_ABSENT_CLUSTER_LITERAL_CANARY\"}";
    internal const string Headers = "{\"trace\":[\"α\",null],\"n\":null}";
    internal const string UnavailableDetail = "The credential is unavailable or expired.";
    internal const string ChangedPolicyDetail = "The principal policy changed since this command was evaluated.";
    internal const string TenantPrefix = "safe-sensitive-";
    internal const string Database = "database";
    internal const string Domain = "messaging";
    internal const string WorkerPrefix = "safe-sensitive-worker-";
    internal const string CredentialSuffix = ".keyload-execution-sensitive-credential";
    internal const string SubscriptionChangedDetail = "A subscription principal policy changed after delivery.";
    internal const string McpUnavailableDetail = "The request has no valid database credential.";
    internal const string McpDeniedDetail = "The authenticated principal is not permitted to perform this operation.";
    internal const string AckKind = "Ack";
    internal const int EmptyCount = 0;
    internal const long FirstPosition = 1;
    internal const long SecondPosition = 2;
    internal const long FirstLeaseVersion = 1;
    internal const long ReadyVersion = 1;
    internal const long LeasedVersion = 2;
    internal const long AckedVersion = 3;
    internal const int PlacementSchema = 1;
    internal const long InitialRevision = 1;
    internal const long RevokedEpoch = 2;
    internal const long RepairedEpoch = 3;
    internal const Capability WorkerCapabilities = Capability.QueueConsume | Capability.QueueAck | Capability.QueueInspect
        | Capability.DeadLettersRead | Capability.TopicsRead | Capability.SubscriptionsConsume | Capability.SubscriptionsAck;
}
