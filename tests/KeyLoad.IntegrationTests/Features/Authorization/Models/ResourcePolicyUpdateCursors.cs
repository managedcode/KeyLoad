using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.Authorization;

/// <summary>Retains real pre-update SDK and MCP cursors for cross-boundary invalidation assertions.</summary>
internal sealed record ResourcePolicyUpdateCursors(string SdkChange, string McpChange, AstQueryRequest LiveRequest,
    string SdkLive, string McpLive);
