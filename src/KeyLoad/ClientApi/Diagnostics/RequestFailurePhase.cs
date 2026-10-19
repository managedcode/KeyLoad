namespace KeyLoad.Server;

/// <summary>Closed request failure dispatch phases suitable for internal diagnostics.</summary>
internal enum RequestFailurePhase
{
    /// <summary>No dispatch marker was set before the failure.</summary>
    None,
    /// <summary>The request failed while dispatching the credential.</summary>
    CredentialDispatch,
    /// <summary>The request failed while dispatching a read.</summary>
    ReadDispatch,
    /// <summary>The request failed while dispatching a command.</summary>
    CommandDispatch
}
