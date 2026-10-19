namespace KeyLoad.Server;

/// <summary>Closed failure categories for a execution request stream before a validated public response.</summary>
internal enum OrleansRpcFailureCategory
{
    /// <summary>The execution Orleans runtime could not complete the call.</summary>
    Orleans,
    /// <summary>The initial call exceeded its execution response deadline.</summary>
    Timeout,
    /// <summary>The execution enumeration was cancelled.</summary>
    Cancellation,
    /// <summary>The stream did not satisfy the closed product protocol.</summary>
    Protocol,
    /// <summary>Another nonfatal failure interrupted enumeration or cleanup.</summary>
    Unexpected
}
