namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventAppendRf3Protocol
{
    internal const int FirstEventIndex = 0;
    internal const int SecondEventIndex = 1;
    internal const long SeedRevision = 3;
    internal const long WinnerRevision = 4;
    internal const long HealthyRevision = 5;
    internal const long FirstRevision = 1;
    internal const long InitialRevision = 0;
    internal const int PageLimit = 5;
    internal const string DeniedDetail = "The principal cannot perform this operation in this scope.";
    internal const string AppendKind = "appendEvents";
    internal const string StaleDetail = "The stream generation is stale.";
    internal const string RevisionDetail = "The expected stream revision does not match.";
    internal const string CommandDetail = "The command ID was already used with different content.";
    internal const string DuplicateDetail = "The event ID is already retained in this source generation.";
    internal const string EventDetail = "The event ID was reused with different content.";
    internal const string MissingProblem = "The public append result has no canonical safe problem.";
    internal const string MissingReceipt = "The public append result has no committed receipt.";
    internal const string SdkEventId = "exact-sdk-event";
    internal const string McpEventId = "exact-mcp-event";
    internal const string PartialEventId = "partial-must-not-appear";
    internal const string ChangedPayload = "{\"changed\":true}";
    internal const string SdkPayload = "{\"producer\":\"sdk\",\"private\":\"mcp-private-event-canary\"}";
    internal const string SdkExpected = "{\"private\":\"mcp-private-event-canary\",\"producer\":\"sdk\"}";
    internal const string McpPayload = "{\"producer\":\"mcp\",\"private\":\"mcp-private-event-canary\"}";
    internal const string McpExpected = "{\"private\":\"mcp-private-event-canary\",\"producer\":\"mcp\"}";
    internal const string HealthyPayload = "{\"producer\":\"healthy\",\"private\":\"mcp-private-event-canary\"}";
    internal const string HealthyExpected = "{\"private\":\"mcp-private-event-canary\",\"producer\":\"healthy\"}";
}
