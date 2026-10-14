namespace KeyLoad.Server;

// Transient local compilation only; never retained in a portal, persisted or sent between grains.
internal readonly record struct SqlNativeSessionCompilation(McpDecodedOperation? Operation, Guid? ContextCommandId, long? ContextOwnershipEpoch = null);
