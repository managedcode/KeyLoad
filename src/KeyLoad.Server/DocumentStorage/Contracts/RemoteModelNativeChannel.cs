namespace KeyLoad.Server.Features.DocumentStorage;

/// <summary>The original model wire carried by a native silo exchange.</summary>
[Alias(RemoteModelNativeProtocol.ChannelAlias)]
internal enum RemoteModelNativeChannel
{
    /// <summary>The unchanged document, controlled document/blob or queue-transfer envelope.</summary>
    DocumentEnvelope = 0,
    /// <summary>The unchanged event-vector wire and original proof.</summary>
    EventVector = 1,
    /// <summary>Read-only authenticated ANN root and original publication observation.</summary>
    AnnProjectionObservation = 2,
    /// <summary>Mandatory authenticated resource-generation coverage for data reads.</summary>
    ResourceLifetimeRead = 3,
    /// <summary>Mandatory same-cut resource-generation and source authority for native ANN reads.</summary>
    ResourceLifetimeAnnRead = 4,
    /// <summary>Effect-sensitive original blob outcome observations with mandatory resource lifetime coverage.</summary>
    ResourceLifetimeBlobOutcome = 5
}
