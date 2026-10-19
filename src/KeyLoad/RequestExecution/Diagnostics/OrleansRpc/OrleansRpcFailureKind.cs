namespace KeyLoad.Server;

/// <summary>Closed content-free observations of the original execution request failure.</summary>
internal enum OrleansRpcFailureKind
{
    /// <summary>The original failure was observed as Other.</summary>
    Other,
    /// <summary>The original failure was observed as Orleans.</summary>
    Orleans,
    /// <summary>The original failure was observed as Timeout.</summary>
    Timeout,
    /// <summary>The original failure was observed as Cancellation.</summary>
    Cancellation,
    /// <summary>The original failure was observed as Protocol.</summary>
    Protocol,
    /// <summary>The original failure was observed as Disposed.</summary>
    Disposed,
    /// <summary>The original failure was observed as InvalidOperation.</summary>
    InvalidOperation,
    /// <summary>The original failure was observed as NotSupported.</summary>
    NotSupported,
    /// <summary>The original failure was observed as Argument.</summary>
    Argument,
    /// <summary>The original failure was observed as Serialization.</summary>
    Serialization,
    /// <summary>The original failure was observed as Aggregate.</summary>
    Aggregate,
    /// <summary>The original failure was observed as InputOutput.</summary>
    InputOutput,
    /// <summary>The original failure was a typed KeyLoad exception; no exception text is retained.</summary>
    KeyLoad,
    /// <summary>The original failure was a JSON exception; no input or parser text is retained.</summary>
    Json,
    /// <summary>The original failure was a format exception; no formatted input is retained.</summary>
    Format
}
