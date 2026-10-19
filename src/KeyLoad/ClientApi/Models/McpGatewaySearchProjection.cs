using System.Collections.Immutable;

namespace KeyLoad.Server;

/// <summary>Contains only complete canonical tools selected by execution gateway search.</summary>
internal sealed record McpGatewaySearchProjection(ImmutableArray<McpGatewayScoredTool> Matches);
