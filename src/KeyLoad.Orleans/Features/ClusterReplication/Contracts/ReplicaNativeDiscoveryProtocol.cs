namespace KeyLoad.Orleans;

/// <summary>Freezes native metadata method and generated message identities.</summary>
public static class ReplicaNativeDiscoveryProtocol
{
    /// <summary>The accepted native discovery message version.</summary>
    public const int Version = 1;
    /// <summary>The stable appended native service method identity.</summary>
    public const string DiscoverAlias = "discover";
    /// <summary>The generated current native request identity.</summary>
    public const string RequestAlias = "keyload.replica.native-discovery-request.v1";
    /// <summary>The generated current native reply identity.</summary>
    public const string ReplyAlias = "keyload.replica.native-discovery-reply.v1";
    internal const string MacFrameAlias = "keyload.replica.native-discovery-mac-frame.v1";
    internal const string RequestPurpose = "keyload.native-discovery-request.v1";
    internal const string ReplyPurpose = "keyload.native-discovery-reply.v1";
}
