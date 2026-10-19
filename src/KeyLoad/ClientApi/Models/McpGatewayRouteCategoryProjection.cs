using System.Collections.Immutable;

namespace KeyLoad.Server;

/// <summary>Contains one execution category and its bounded unique canonical tools.</summary>
internal sealed record McpGatewayRouteCategoryProjection(
    string Category,
    double Score,
    ImmutableArray<McpGatewayScoredTool> Tools);
