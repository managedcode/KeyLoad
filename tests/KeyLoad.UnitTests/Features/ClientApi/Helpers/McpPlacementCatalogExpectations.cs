using System.Collections.Immutable;
using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Independent placement operation identities for the frozen public catalog.</summary>
internal static class McpPlacementCatalogExpectations
{
    internal static ImmutableArray<(string Name, string Route, GrainReadKind? ReadKind, OperationKind? CommandKind)> Entries
    {
        get;
    } =
    [
        ("keyload_admin_placement_policy_configure", "/admin/placement/policy/configure", null, OperationKind.ConfigurePlacementPolicy),
        ("keyload_admin_placement_policy_read", "/admin/placement/policy/read", GrainReadKind.PlacementPolicy, null),
        ("keyload_admin_placement_plan", "/admin/placement/plan", GrainReadKind.PlacementPlan, null),
        ("keyload_admin_placement_intent_persist", "/admin/placement/intent/persist", null, OperationKind.PersistPlacementIntent),
        ("keyload_admin_placement_intent_apply", "/admin/placement/intent/apply", null, OperationKind.ApplyPlacementIntent),
        ("keyload_admin_placement_intent_read", "/admin/placement/intent/read", GrainReadKind.PlacementIntent, null)
    ];
}
