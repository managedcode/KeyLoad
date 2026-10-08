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
    private const string ForeignDocumentIncarnation = "The document session token belongs to another incarnation.";
    private const string DocumentOutOfScope = "The document session token belongs to another atomic partition or placement.";
    private const string DocumentInvalidPosition = "The document session token position must be positive.";
    private const string DocumentFuturePosition = "The document session token is beyond the current quorum-applied cut.";
    private const string SelectedTextMismatch = "The native text projection does not match the authorized source cut.";
    private const string ConfiguredVectorMismatch = "The declared vector profile does not match the configured field.";
    private const string OwnedScopeDenied = "The principal cannot perform this operation in this scope.";
    private const string CommandContentConflict = "The command ID was already used with different content.";
    private const string GenericFailure = "The database operation could not be completed.";
    private const string UnknownOutcome = "The write outcome is unknown. Retry with the same command identity and payload.";
    private const string RecoveryRequired = "The database requires recovery before another operation.";
    private const string ResourceExhausted = "The operation exceeds the available resource capacity.";
    private const string PermissionDenied = "The authenticated principal is not permitted to perform this operation.";
    private const string Unauthenticated = "The request has no valid database credential.";

    /// <summary>Selects an owned safe detail through a closed code/literal mapping, without reflecting arbitrary detail.</summary>
    /// <param name="code">The domain category to explain safely.</param>
    /// <param name="ownedDetail">Actual server detail considered only by the closed code/literal mapping.</param>
    /// <returns>A fixed safe description of the failed operation.</returns>
    internal static string SafeDetail(ErrorCode code, string? ownedDetail = null) => (code, ownedDetail) switch
    {
        (ErrorCode.TokenInvalidated, ForeignDocumentIncarnation) => ForeignDocumentIncarnation,
        (ErrorCode.TokenInvalidated, DocumentOutOfScope) => DocumentOutOfScope,
        (ErrorCode.TokenInvalidated, DocumentInvalidPosition) => DocumentInvalidPosition,
        (ErrorCode.TokenInvalidated, DocumentFuturePosition) => DocumentFuturePosition,
        (ErrorCode.HistoryUnavailable, SelectedTextMismatch) => SelectedTextMismatch,
        (ErrorCode.Validation, ConfiguredVectorMismatch) => ConfiguredVectorMismatch,
        (ErrorCode.PermissionDenied, OwnedScopeDenied) => OwnedScopeDenied,
        (ErrorCode.Conflict, CommandContentConflict) => CommandContentConflict,
        _ => CodeDetail(code)
    };

    private static string CodeDetail(ErrorCode code) => code switch
    {
        ErrorCode.UnknownWriteOutcome => UnknownOutcome,
        ErrorCode.RecoveryRequired => RecoveryRequired,
        ErrorCode.ResourceExhausted or ErrorCode.BudgetExceeded => ResourceExhausted,
        ErrorCode.PermissionDenied => PermissionDenied,
        ErrorCode.Unauthenticated => Unauthenticated,
        _ => GenericFailure
    };
}
