namespace KeyLoad.Server.Features.DocumentStorage;

/// <summary>Local provenance for an actually awaited native grain terminal error; never a wire receipt.</summary>
internal sealed class RemoteModelNativeTerminalException : Exception
{
    /// <summary>Creates an exception without observed native terminal provenance.</summary>
    public RemoteModelNativeTerminalException() { }

    /// <summary>Creates an exception without observed native terminal provenance.</summary>
    /// <param name="message">The diagnostic message.</param>
    public RemoteModelNativeTerminalException(string? message) : base(message) { }

    /// <summary>Retains a cause without treating it as observed native terminal provenance.</summary>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="innerException">The original cause.</param>
    public RemoteModelNativeTerminalException(string? message, Exception? innerException)
        : base(message, innerException) { }

    internal RemoteModelNativeTerminalException(KeyLoadException original)
        : base(original?.Message ?? throw new ArgumentNullException(nameof(original)), original)
    { Original = original; }

    internal KeyLoadException? Original { get; }
}
