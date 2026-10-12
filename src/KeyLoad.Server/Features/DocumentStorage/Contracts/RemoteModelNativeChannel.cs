namespace KeyLoad.Server.Features.DocumentStorage;

/// <summary>The original model wire carried by a native silo exchange.</summary>
[Alias(RemoteModelNativeProtocol.ChannelAlias)]
internal enum RemoteModelNativeChannel
{
    /// <summary>The unchanged document, controlled document/blob or queue-transfer envelope.</summary>
    DocumentEnvelope = 0,
    /// <summary>The unchanged event-vector wire and original proof.</summary>
    EventVector = 1
}
