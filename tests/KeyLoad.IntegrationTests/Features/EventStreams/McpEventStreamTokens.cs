namespace KeyLoad.IntegrationTests.Features.EventStreams;

/// <summary>Named public values for genuine EventStreams RF3 callers.</summary>
internal static class McpEventStreamTokens
{
    internal const string TenantPrefix = "mcp-event-tenant-";
    internal const string ForeignTenant = "mcp-event-foreign-tenant";
    internal const string Database = "mcp-event-database";
    internal const string Domain = "mcp-event-domain";
    internal const string StreamSet = "mcp-event-streams";
    internal const string StreamId = "mcp-order-stream";
    internal const string EventIdCreated = "mcp-event-created";
    internal const string EventIdPaid = "mcp-event-paid";
    internal const string EventIdShipped = "mcp-event-shipped";
    internal const string EventType = "OrderLifecycleChanged";
    internal const string PrivateMarker = "mcp-private-event-canary";
    internal const string InitialPayload = "{\"status\":\"created\",\"private\":\"mcp-private-event-canary\"}";
    internal const string PaidPayload = "{\"status\":\"paid\",\"private\":\"mcp-private-event-canary\"}";
    internal const string ShippedPayload = "{\"status\":\"shipped\",\"private\":\"mcp-private-event-canary\"}";
    internal const string InitialHeaders = "{\"source\":\"checkout\",\"sequence\":1}";
    internal const string PaidHeaders = "{\"source\":\"billing\",\"sequence\":2}";
    internal const string ShippedHeaders = "{\"source\":\"fulfillment\",\"sequence\":3}";
    internal const string CanonicalInitialPayload = "{\"private\":\"mcp-private-event-canary\",\"status\":\"created\"}";
    internal const string CanonicalPaidPayload = "{\"private\":\"mcp-private-event-canary\",\"status\":\"paid\"}";
    internal const string CanonicalShippedPayload = "{\"private\":\"mcp-private-event-canary\",\"status\":\"shipped\"}";
    internal const string CanonicalInitialHeaders = "{\"sequence\":1,\"source\":\"checkout\"}";
    internal const string CanonicalPaidHeaders = "{\"sequence\":2,\"source\":\"billing\"}";
    internal const string CanonicalShippedHeaders = "{\"sequence\":3,\"source\":\"fulfillment\"}";
    internal const string CorrelationId = "mcp-event-correlation";
    internal const string CausationIdCreated = "mcp-event-cause-created";
    internal const string CausationIdPaid = "mcp-event-cause-paid";
    internal const string CausationIdShipped = "mcp-event-cause-shipped";
    internal const string GuidFormat = "N";
    internal const int FirstPageLimit = 2;
    internal const int EmptyTailLimit = 2;
    internal const int InvalidPageLimit = 0;
    internal const int InitialRevision = 0;
    internal const int InitialCutPosition = 0;
    internal const int FirstEventRevision = 1;
    internal const int SecondEventRevision = 2;
    internal const int StreamGeneration = 1;
    internal const int EventSchemaVersion = 1;
    internal const int StaleGeneration = 2;
    internal const int ExpectedEventCount = 3;
    internal const int LastEventRevision = 3;
    internal const int ExpectedFirstPageCount = 2;
    internal const int ExpectedSecondPageCount = 1;
    internal const int ExpectedPageCallCount = 3;
    internal const int ExpectedProtectedCallCount = 5;
    internal const long FirstExpectedSequence = 1;
    internal const long SecondExpectedSequence = 2;
    internal const long ThirdExpectedSequence = 3;
    internal static readonly DateTimeOffset OccurredAt = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
}
