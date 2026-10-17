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
