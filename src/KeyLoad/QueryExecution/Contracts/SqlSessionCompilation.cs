namespace KeyLoad.Server;

// Transient local compilation only; never retained in a portal, persisted or sent between grains.
internal readonly record struct SqlSessionCompilation(McpDecodedOperation? Operation, Guid? ContextCommandId);
