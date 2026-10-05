namespace KeyLoad.Orleans;

/// <summary>Identifies one of the finite local native-request work classes.</summary>
internal enum NativeRequestWorkKind
{
    /// <summary>A request-grain native stream producer.</summary>
    RequestProducer,
    /// <summary>A verified read capability invocation.</summary>
    ReadCapability,
    /// <summary>A verified command capability invocation.</summary>
    CommandCapability
}
