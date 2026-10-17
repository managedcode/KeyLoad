using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record NativeTextCapturedRf3Observation(RankedDocument[]? Rows, string? SdkError,
    CallToolResult? McpError, Exception? TransportFailure);
