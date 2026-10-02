namespace KeyLoad.Server;

/// <summary>Defines fixed output fields and safe owned failure details without retaining caller data.</summary>
internal static class McpReplyProtocol
{
    internal const string Result = "result";
    internal const string Error = "error";
    internal const string RequestId = "requestId";
    internal const string SuccessSummary = "The database operation completed.";
    internal const string FailureSummary = "The database operation failed.";
    internal const string NativeOutputExceeded = "The native MCP response exceeds its byte or structural budget.";
    private const string GenericFailure = "The database operation could not be completed.";
    private const string UnknownOutcome = "The write outcome is unknown. Retry with the same command identity and payload.";
    private const string RecoveryRequired = "The database requires recovery before another operation.";
    private const string ResourceExhausted = "The operation exceeds the available resource capacity.";
    private const string PermissionDenied = "The authenticated principal is not permitted to perform this operation.";
    private const string Unauthenticated = "The request has no valid database credential.";

    /// <summary>Selects an owned safe detail without inspecting or retaining an exception or user payload.</summary>
    /// <param name="code">The domain category to explain safely.</param>
    /// <returns>A fixed safe description of the failed operation.</returns>
    internal static string SafeDetail(ErrorCode code) => code switch
    {
        ErrorCode.UnknownWriteOutcome => UnknownOutcome,
        ErrorCode.RecoveryRequired => RecoveryRequired,
        ErrorCode.ResourceExhausted or ErrorCode.BudgetExceeded => ResourceExhausted,
        ErrorCode.PermissionDenied => PermissionDenied,
        ErrorCode.Unauthenticated => Unauthenticated,
        _ => GenericFailure
    };
}
