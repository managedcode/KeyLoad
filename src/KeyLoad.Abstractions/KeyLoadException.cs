using ManagedCode.Communication;

namespace KeyLoad;

/// <summary>Represents a safe, caller-visible KeyLoad failure with its domain code and HTTP status.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(KeyLoadException.NativeAlias)]
public sealed class KeyLoadException : Exception
{
    internal const string NativeAlias = "keyload.error.v1";
    private const string DefaultSafeDetail = "A KeyLoad operation failed.";
    /// <summary>Initializes a KeyLoad exception with the supplied domain code, safe detail, and HTTP status.</summary>
    /// <param name="code">Identifies the domain error.</param>
    /// <param name="safeDetail">Provides safe caller-visible detail.</param>
    /// <param name="statusCode">Provides the HTTP status associated with the error.</param>
    public KeyLoadException(ErrorCode code, string safeDetail, int statusCode)
        : this(code, safeDetail, statusCode, null)
    {
    }

    /// <summary>Initializes a validation exception with a standard safe detail.</summary>
    public KeyLoadException() : this(ErrorCode.Validation, DefaultSafeDetail, Errors.Status(ErrorCode.Validation), null)
    {
    }

    /// <summary>Initializes a validation exception with the supplied message.</summary>
    /// <param name="message">Provides safe caller-visible failure detail.</param>
    public KeyLoadException(string message) : this(ErrorCode.Validation, message, Errors.Status(ErrorCode.Validation), null)
    {
    }

    /// <summary>Initializes a validation exception with the supplied message and cause.</summary>
    /// <param name="message">Provides safe caller-visible failure detail.</param>
    /// <param name="innerException">Provides the exception that caused this failure.</param>
    public KeyLoadException(string message, Exception innerException)
        : this(ErrorCode.Validation, message, Errors.Status(ErrorCode.Validation), innerException)
    {
    }

    internal KeyLoadException(ErrorCode code, string safeDetail, int statusCode, Exception? innerException)
        : base(safeDetail, innerException)
    {
        Code = code;
        StatusCode = statusCode;
    }

    /// <summary>Gets the domain error code.</summary>
    [Orleans.Id(0)]
    public ErrorCode Code { get; }

    /// <summary>Gets the corresponding HTTP status code.</summary>
    [Orleans.Id(1)]
    public int StatusCode { get; }

    /// <summary>Creates the safe problem detail for this failure.</summary>
    /// <returns>The problem detail associated with this exception.</returns>
    public Problem ToProblem() => Errors.Problem(Code, Message);
}
