using System.Collections.Immutable;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal static class TenantPlacementMcpCatalog
{
    internal static ImmutableArray<McpOperationDescriptor> Entries { get; } =
    [
        McpOperationFactory.HeaderCommand<ConfigurePlacementPolicyRequest, TenantPlacementPolicyV1>(
            TenantPlacementPublicProtocol.ConfigureTool, TenantPlacementPublicProtocol.ConfigureRoute, OperationKind.ConfigurePlacementPolicy),
        McpOperationFactory.Read<TenantPlacementScope, TenantPlacementPolicyV1>(
            TenantPlacementPublicProtocol.PolicyTool, TenantPlacementPublicProtocol.PolicyRoute, GrainReadKind.PlacementPolicy),
        McpOperationFactory.Read<TenantPlacementPlanRequest, TenantPlacementPlanResultV1>(
            TenantPlacementPublicProtocol.PlanTool, TenantPlacementPublicProtocol.PlanRoute, GrainReadKind.PlacementPlan),
        McpOperationFactory.HeaderCommand<PersistPlacementIntentRequest, PlacementIntentV1>(
            TenantPlacementPublicProtocol.PersistTool, TenantPlacementPublicProtocol.PersistRoute, OperationKind.PersistPlacementIntent),
        McpOperationFactory.HeaderCommand<ApplyPlacementIntentRequest, PlacementIntentV1>(
            TenantPlacementPublicProtocol.ApplyTool, TenantPlacementPublicProtocol.ApplyRoute, OperationKind.ApplyPlacementIntent),
        McpOperationFactory.Read<PlacementIntentReadRequest, PlacementIntentV1>(
            TenantPlacementPublicProtocol.IntentTool, TenantPlacementPublicProtocol.IntentRoute, GrainReadKind.PlacementIntent)
    ];
}
