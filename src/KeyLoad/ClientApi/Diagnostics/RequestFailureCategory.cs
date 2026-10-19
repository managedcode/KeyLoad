namespace KeyLoad.Server;

/// <summary>Closed exception categories suitable for internal diagnostics.</summary>
internal enum RequestFailureCategory
{
    /// <summary>A KeyLoad domain exception reached the generic failure boundary.</summary>
    KeyLoad,
    /// <summary>A JSON exception reached the generic failure boundary.</summary>
    Json,
    /// <summary>An ASP.NET bad-request exception reached the generic failure boundary.</summary>
    BadHttpRequest,
    /// <summary>An operation cancellation was not caused by caller cancellation.</summary>
    OperationCanceled,
    /// <summary>An argument exception reached the generic failure boundary.</summary>
    Argument,
    /// <summary>An invalid-operation exception reached the generic failure boundary.</summary>
    InvalidOperation,
    /// <summary>An I/O exception reached the generic failure boundary.</summary>
    Io,
    /// <summary>A execution Orleans runtime exception reached the generic failure boundary.</summary>
    Orleans,
    /// <summary>A timeout exception reached the generic failure boundary.</summary>
    Timeout,
    /// <summary>An exception outside the known bounded categories reached the boundary.</summary>
    Other
}
