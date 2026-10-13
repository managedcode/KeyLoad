namespace KeyLoad.AppHost.Features.ClusterRouting;

internal static class TenantCapacityPlacementProfile
{
    internal const string Key = "TenantCapacityPlacement";
    internal const string Setting = "KeyLoadTests:ClusterRouting:TenantCapacityPlacement";
    internal const string Environment = "KeyLoad__TenantPlacementExecution__Enabled";
    private const string Enabled = "true";
    private const string Disabled = "false";

    internal static bool Validate(string? original, string? topology, string? movement, string? query, string? registration)
    {
        if (original is null || original == Disabled)
        { return false; }
        if (original != Enabled
            || topology != TwoRf3ProfileProtocol.Profile
            || movement != Enabled
            || query != Enabled
            || registration != Enabled)
        { throw new InvalidOperationException(TwoRf3ProfileProtocol.Invalid); }
        return true;
    }
}
