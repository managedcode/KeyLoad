namespace KeyLoad.Server;

/// <summary>Closed failure categories for a native request stream before a validated public response.</summary>
internal enum OrleansRpcFailureCategory
{
    /// <summary>The native Orleans runtime could not complete the call.</summary>
    Orleans,
    /// <summary>The initial call exceeded its native response deadline.</summary>
    Timeout,
    /// <summary>The native enumeration was cancelled.</summary>
    Cancellation,
    /// <summary>The stream did not satisfy the closed product protocol.</summary>
    Protocol,
    /// <summary>Another nonfatal failure interrupted enumeration or cleanup.</summary>
    Unexpected
}
