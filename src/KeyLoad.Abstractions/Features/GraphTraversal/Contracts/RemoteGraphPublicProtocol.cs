namespace KeyLoad;

/// <summary>Explicit remote physical-owner graph public routes and on-demand tools.</summary>
public static class RemoteGraphPublicProtocol
{
    /// <summary>POST route for an original remote source edge upsert.</summary>
    public const string UpsertRoute = "/v1/graph/remote/upsert";
    /// <summary>POST route for an original remote source edge deletion.</summary>
    public const string DeleteRoute = "/v1/graph/remote/delete";
    /// <summary>POST route for explicit eventual reverse-edge delivery.</summary>
    public const string DeliverRoute = "/v1/graph/remote/deliver";
    /// <summary>POST route for one remote eventual incoming page.</summary>
    public const string IncomingRoute = "/v1/graph/remote/incoming";
    /// <summary>Canonical on-demand upsert tool.</summary>
    public const string UpsertTool = "keyload_graph_remote_upsert";
    /// <summary>Canonical on-demand delete tool.</summary>
    public const string DeleteTool = "keyload_graph_remote_delete";
    /// <summary>Canonical on-demand explicit delivery tool.</summary>
    public const string DeliverTool = "keyload_graph_remote_deliver";
    /// <summary>Canonical on-demand incoming projection tool.</summary>
    public const string IncomingTool = "keyload_graph_remote_incoming";
}
