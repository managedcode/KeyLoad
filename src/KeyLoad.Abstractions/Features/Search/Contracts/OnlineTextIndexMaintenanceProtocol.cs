namespace KeyLoad;

/// <summary>Names the distinct administrator-authorized online text publication operation.</summary>
public static class OnlineTextIndexMaintenanceProtocol
{
    internal const string RequestAlias = "keyload.search.online-text-index-maintenance-request.v1";
    internal const string ResultAlias = "keyload.search.online-text-index-maintenance-result.v1";
    /// <summary>Authenticated HTTP route for one original bounded maintenance operation.</summary>
    public const string Route = "/v1/search/text/online/maintain";
    /// <summary>Discoverable operation tool, also callable through the existing Q1 operation protocol.</summary>
    public const string Tool = "keyload_search_text_online_maintain";
    /// <summary>Bounded native discovery guidance without caller data or credentials.</summary>
    public const string Description = "Publish a bounded online text generation for an already administrator-configured consumer under fresh persisted administrator, data and field authority. Existing implicit search remains read-only. Old derived files retire only after original reader leases join; retain the same command ID and payload for original-result replay.";
}

