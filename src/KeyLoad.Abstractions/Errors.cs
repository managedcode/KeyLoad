using ManagedCode.Communication;

namespace KeyLoad;

/// <summary>Creates standard status mappings, problem details, and KeyLoad exceptions.</summary>
public static class Errors
{
    private const string ProblemTypePrefix = "urn:keyload:error:";
    /// <summary>Returns the HTTP status code associated with an error code.</summary>
    /// <param name="code">Specifies the code value.</param>
    /// <returns>The requested value.</returns>
    public static int Status(ErrorCode code) => code switch
    {
        ErrorCode.NotFound => (int)System.Net.HttpStatusCode.NotFound,
        ErrorCode.PermissionDenied => (int)System.Net.HttpStatusCode.Forbidden,
        ErrorCode.Unauthenticated => (int)System.Net.HttpStatusCode.Unauthorized,
        ErrorCode.Conflict or ErrorCode.RevisionConflict or ErrorCode.DuplicateEventId or ErrorCode.StaleLease => (int)System.Net.HttpStatusCode.Conflict,
        ErrorCode.ResourceExhausted or ErrorCode.BudgetExceeded => (int)System.Net.HttpStatusCode.TooManyRequests,
        ErrorCode.UnsupportedCapability => (int)System.Net.HttpStatusCode.UnprocessableEntity,
        ErrorCode.UnknownWriteOutcome or ErrorCode.RecoveryRequired or ErrorCode.OwnershipLost or ErrorCode.ClockUncertain => (int)System.Net.HttpStatusCode.ServiceUnavailable,
        _ => (int)System.Net.HttpStatusCode.BadRequest
    };
    /// <summary>Creates a problem detail with the standard status for an error code.</summary>
    /// <param name="code">Identifies the domain error code.</param>
    /// <param name="detail">Provides safe caller-visible error detail.</param>
    /// <returns>The standard problem details for the error code.</returns>
    public static Problem Problem(ErrorCode code, string detail) => new()
    {
        Type = $"{ProblemTypePrefix}{code}",
        Title = code.ToString(),
        ErrorCode = code.ToString(),
        Detail = detail,
        StatusCode = Status(code)
    };
    /// <summary>Creates a KeyLoad exception with the status assigned to its error code.</summary>
    /// <param name="code">Identifies the domain error code.</param>
    /// <param name="detail">Provides safe caller-visible error detail.</param>
    /// <returns>A KeyLoad exception with the matching HTTP status.</returns>
    public static KeyLoadException Fail(ErrorCode code, string detail) => new(code, detail, Status(code));
    /// <summary>Creates a safe typed failure while retaining the complete original cause.</summary>
    /// <param name="code">Identifies the domain error.</param>
    /// <param name="detail">Provides safe caller-visible detail.</param>
    /// <param name="cause">Retains the original settled operation failure.</param>
    /// <returns>The typed failure with its original cause.</returns>
    public static KeyLoadException Fail(ErrorCode code, string detail, Exception cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        return new(code, detail, Status(code), cause);
    }

}
