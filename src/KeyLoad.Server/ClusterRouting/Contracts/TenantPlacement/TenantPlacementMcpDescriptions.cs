namespace KeyLoad.Server;

internal static class TenantPlacementMcpDescriptions
{
    private const string ConfigureDescription = "Persist an administrator-authorized capacity policy by exact revision CAS.";
    private const string PolicyDescription = "Read the current persisted placement policy under fresh administrator authorization.";
    private const string PlanDescription = "Plan complete supplied whole partitions from bounded signed original six-owner samples.";
    private const string PersistDescription = "Persist an original unexpired signed plan and immutable Move identities at Control RF3.";
    private const string ApplyDescription = "Resume the exact original intent through native Move receipts; interruption may leave its original outcome unknown.";
    private const string IntentDescription = "Read the current durable intent, original apply identity and completed native Move receipts.";

    internal static string? Find(string name) => name switch
    {
        TenantPlacementPublicProtocol.ConfigureTool => ConfigureDescription,
        TenantPlacementPublicProtocol.PolicyTool => PolicyDescription,
        TenantPlacementPublicProtocol.PlanTool => PlanDescription,
        TenantPlacementPublicProtocol.PersistTool => PersistDescription,
        TenantPlacementPublicProtocol.ApplyTool => ApplyDescription,
        TenantPlacementPublicProtocol.IntentTool => IntentDescription,
        _ => null
    };
}
