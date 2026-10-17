using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Pairs one fresh official tool schema with its finite native relevance score.</summary>
internal sealed record McpGatewayScoredTool(Tool Tool, double Score);
