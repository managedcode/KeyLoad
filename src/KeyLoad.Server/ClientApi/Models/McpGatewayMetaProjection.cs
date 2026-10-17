using System.Collections.Immutable;
using ModelContextProtocol.Protocol;

namespace KeyLoad.Server;

/// <summary>Contains only complete canonical tools selected by native gateway search.</summary>
internal sealed record McpGatewaySearchProjection(ImmutableArray<McpGatewayScoredTool> Matches);

/// <summary>Contains native routing categories mapped to canonical operation metadata.</summary>
internal sealed record McpGatewayRouteProjection(ImmutableArray<McpGatewayRouteCategoryProjection> Categories);

/// <summary>Pairs one fresh official tool schema with its finite native relevance score.</summary>
internal sealed record McpGatewayScoredTool(Tool Tool, double Score);

/// <summary>Contains one native category and its bounded unique canonical tools.</summary>
internal sealed record McpGatewayRouteCategoryProjection(
    string Category,
    double Score,
    ImmutableArray<McpGatewayScoredTool> Tools);
