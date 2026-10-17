using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Describes only the outcome that the official MCP client actually exposed.</summary>
internal sealed record RequestCqrsFaultMcpObservation(CallToolResult? ToolResult, Exception? TransportFailure);
