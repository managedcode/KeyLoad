using System.Collections.Immutable;

namespace KeyLoad.Server;

/// <summary>Contains native routing categories mapped to canonical operation metadata.</summary>
internal sealed record McpGatewayRouteProjection(ImmutableArray<McpGatewayRouteCategoryProjection> Categories);
