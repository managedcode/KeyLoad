namespace KeyLoad.Clustering;

/// <summary>Identifies one of the finite local execution-request work classes.</summary>
internal enum RequestWorkKind
{
    /// <summary>A request-grain execution stream producer.</summary>
    RequestProducer,
    /// <summary>A verified read capability invocation.</summary>
    ReadCapability,
    /// <summary>A verified command capability invocation.</summary>
    CommandCapability,
    /// <summary>The actual authenticated outer execution transport and its reply publication.</summary>
    RuntimeTransport
}
