namespace KeyLoad;

/// <summary>Defines the current explicit queue dispatch control wire identities.</summary>
public static class QueueDispatchControlProtocol
{
    /// <summary>The stable generated request serialization alias.</summary>
    public const string RequestAlias = "keyload.messaging.queue-dispatch-control-request";
    /// <summary>The bounded official MCP operation name.</summary>
    public const string Tool = "keyload_queue_dispatch_control";
    /// <summary>The exact bounded discovery description of the version-fenced effect.</summary>
    public const string Description = "Pause or resume one existing work queue as a persisted cluster administrator. The expectedSchemaVersion must match current metadata; only pause state and the next version change. Supply a stable outer commandId and preserve its canonical request for replay.";
    /// <summary>The authenticated SDK command route.</summary>
    public const string Route = "/admin/queues/dispatch";
}
