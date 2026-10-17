namespace KeyLoad;

/// <summary>Defines the bounded TenantPlacementPublicProtocol contract.</summary>
public static class TenantPlacementPublicProtocol
{
    /// <summary>Defines the stable public protocol value.</summary>
    public const string ConfigureRoute = "/admin/placement/policy/configure";
    /// <summary>Defines the stable public protocol value.</summary>
    public const string PolicyRoute = "/admin/placement/policy/read";
    /// <summary>Defines the stable public protocol value.</summary>
    public const string PlanRoute = "/admin/placement/plan";
    /// <summary>Defines the stable public protocol value.</summary>
    public const string PersistRoute = "/admin/placement/intent/persist";
    /// <summary>Defines the stable public protocol value.</summary>
    public const string ApplyRoute = "/admin/placement/intent/apply";
    /// <summary>Defines the stable public protocol value.</summary>
    public const string IntentRoute = "/admin/placement/intent/read";
    /// <summary>Defines the stable public protocol value.</summary>
    public const string ConfigureTool = "keyload_admin_placement_policy_configure";
    /// <summary>Defines the stable public protocol value.</summary>
    public const string PolicyTool = "keyload_admin_placement_policy_read";
    /// <summary>Defines the stable public protocol value.</summary>
    public const string PlanTool = "keyload_admin_placement_plan";
    /// <summary>Defines the stable public protocol value.</summary>
    public const string PersistTool = "keyload_admin_placement_intent_persist";
    /// <summary>Defines the stable public protocol value.</summary>
    public const string ApplyTool = "keyload_admin_placement_intent_apply";
    /// <summary>Defines the stable public protocol value.</summary>
    public const string IntentTool = "keyload_admin_placement_intent_read";
    /// <summary>Defines the stable public protocol value.</summary>
    public const string Interrupted = "The original placement intent outcome is unknown; retain its intent and movement identities.";
}

/// <summary>Defines the bounded TenantPlacementScope contract.</summary>
/// <param name="TenantId">The original TenantId contract value.</param>
/// <param name="DatabaseId">The original DatabaseId contract value.</param>
/// <param name="ControlPartition">The original ControlPartition contract value.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(TenantPlacementPublicProtocolAliases.TenantPlacementScopeAlias)]
public sealed record TenantPlacementScope(
    [property: global::Orleans.Id(0)] string TenantId,
    [property: global::Orleans.Id(1)] string DatabaseId,
    [property: global::Orleans.Id(2)] PartitionRef ControlPartition);

/// <summary>Defines the bounded PlacementIntentReadRequest contract.</summary>
/// <param name="Scope">The original Scope contract value.</param>
/// <param name="IntentId">The original IntentId contract value.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(TenantPlacementPublicProtocolAliases.PlacementIntentReadRequestAlias)]
public sealed record PlacementIntentReadRequest(
    [property: global::Orleans.Id(0)] TenantPlacementScope Scope,
    [property: global::Orleans.Id(1)] Guid IntentId);

/// <summary>Defines the bounded PlacementIntentProgressV1 contract.</summary>
/// <param name="IntentId">The original IntentId contract value.</param>
/// <param name="Revision">The original Revision contract value.</param>
/// <param name="CompletedMoves">The original CompletedMoves contract value.</param>
/// <param name="TotalMoves">The original TotalMoves contract value.</param>
/// <param name="OriginalMoveId">The original OriginalMoveId contract value.</param>
/// <param name="ActualPhase">The original ActualPhase contract value.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(TenantPlacementPublicProtocolAliases.PlacementIntentProgressV1Alias)]
public sealed record PlacementIntentProgressV1(
    [property: global::Orleans.Id(0)] Guid IntentId,
    [property: global::Orleans.Id(1)] long Revision,
    [property: global::Orleans.Id(2)] int CompletedMoves,
    [property: global::Orleans.Id(3)] int TotalMoves,
    [property: global::Orleans.Id(4)] Guid OriginalMoveId,
    [property: global::Orleans.Id(5)] PartitionMovePhase ActualPhase);

internal static class TenantPlacementPublicProtocolAliases
{
    internal const string TenantPlacementScopeAlias = "keyload.cluster-routing.tenant-placement-scope.v1";
    internal const string PlacementIntentReadRequestAlias = "keyload.cluster-routing.placement-intent-read-request.v1";
    internal const string PlacementIntentProgressV1Alias = "keyload.cluster-routing.placement-intent-progress.v1";
}
