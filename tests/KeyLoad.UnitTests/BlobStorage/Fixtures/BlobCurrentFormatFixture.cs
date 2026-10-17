using KeyLoad.Core;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal static class BlobCurrentFormatFixture
{
    internal const string Principal = "root";
    internal const string Collection = "orders";
    internal const string DocumentId = "current-format-document";
    internal const string DocumentJson = "{\"value\":\"current-format\"}";
    internal const string BlobId = "current-format-object";
    internal const long OriginalRevision = 1;
    internal const int InitialRevision = 0;
    internal const int PartOrdinal = 0;
    internal const int PartCount = 1;
    internal const int Offset = 2;
    internal const int Count = 3;
    internal const long DefaultMaxBlobBytes = 67_108_864;
    internal const int ExceededBy = 1;
    internal const string InvalidDetail = "The blob request or policy is invalid.";
    internal const string Payload = "abcdefgh";
    internal const string PartialPayload = "cde";
    internal static readonly byte[] Bytes = System.Text.Encoding.UTF8.GetBytes(Payload);
    internal const string CollectionJson = "{\"name\":\"orders\",\"kind\":\"Collection\",\"transactionDomainId\":\"orders\","
        + "\"indexes\":[],\"fieldPolicies\":[],\"headerPolicies\":[],"
        + "\"queuePolicy\":{\"maxAttempts\":5,\"maxStoredMessages\":100000,\"maxStoredBytes\":1073741824,"
        + "\"maxInFlightMessages\":1000,\"maxInFlightBytes\":67108864,\"maxLeaseSeconds\":300,"
        + "\"retryBaseMilliseconds\":1000,\"retryMaxMilliseconds\":300000,"
        + "\"maxDeadLetterMessages\":null,\"maxDeadLetterBytes\":null,"
        + "\"orderingProfile\":\"CompetingConsumers\",\"parkedHeadPolicy\":\"Continue\","
        + "\"retryJitter\":\"None\",\"retryExponentialFactor\":2},"
        + "\"eventRetention\":{\"maxEvents\":100000,\"maxBytes\":1073741824},"
        + "\"authority\":\"Document\",\"schemaVersion\":1,\"paused\":false,\"vectorProfiles\":[]}";

    internal static DatabaseEngine Reopened(ZoneTreeStore store)
        => new(store, new AuthorizationPolicy(), UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.DueWork(),
            UnitExecutionOptions.EventSource(), UnitExecutionOptions.Messaging(), UnitExecutionOptions.GraphExecution(),
            UnitExecutionOptions.ChangeFeedExecution(), UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(),
            UnitExecutionOptions.TimeSeriesExecution(), UnitExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
}
