namespace KeyLoad.Server;

/// <summary>Closed internal stages for a rejected MCP transport guard check.</summary>
internal enum McpTransportStage
{
    /// <summary>Metadata was absent or did not name a defined stage.</summary>
    Unknown,
    /// <summary>The protocol revision header count was not exactly one.</summary>
    ProtocolRevisionCount,
    /// <summary>The single protocol revision header value did not match the configured revision.</summary>
    ProtocolRevisionValue,
    /// <summary>A session header was present on the stateless transport.</summary>
    SessionHeaderPresence,
    /// <summary>A last-event header was present on the stateless transport.</summary>
    LastEventHeaderPresence,
    /// <summary>A method or target header had an invalid count, empty value or length.</summary>
    MethodHeaderShape,
    /// <summary>A method header contained an invalid encoded character.</summary>
    MethodHeaderEncoding,
    /// <summary>A target header had an invalid count, empty value or length.</summary>
    NameHeaderShape,
    /// <summary>A target header had invalid encoding or an invalid encoded wrapper.</summary>
    NameHeaderEncoding,
    /// <summary>The body method field was present with a non-string value.</summary>
    BodyMethodShape,
    /// <summary>The body method did not match the checked method header.</summary>
    BodyMethodMismatch,
    /// <summary>A routed body did not contain the requested target parameter.</summary>
    MissingTarget,
    /// <summary>The params protocol revision did not match the configured revision.</summary>
    ParameterRevision,
    /// <summary>The _meta protocol revision did not match the configured revision.</summary>
    MetadataRevision,
    /// <summary>A present body target did not match the checked target header.</summary>
    TargetMismatch
}
