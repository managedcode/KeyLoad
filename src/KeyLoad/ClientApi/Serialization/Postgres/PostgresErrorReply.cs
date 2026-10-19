using System.Text;

namespace KeyLoad.Server;

internal static class PostgresErrorReply
{
    internal static void Write(PostgresWireWriter writer, Exception failure)
    {
        var code = failure is KeyLoadException known ? known.Code
            : failure is OperationCanceledException ? ErrorCode.Cancelled : (ErrorCode?)null;
        var state = code switch
        {
            ErrorCode.Unauthenticated => InvalidAuthorization,
            ErrorCode.PermissionDenied => InsufficientPrivilege,
            ErrorCode.Validation => InvalidParameter,
            ErrorCode.UnsupportedCapability or ErrorCode.FormatUnsupported => FeatureUnsupported,
            ErrorCode.Cancelled => QueryCancelled,
            ErrorCode.ResourceExhausted or ErrorCode.BudgetExceeded => ResourcesExhausted,
            ErrorCode.Conflict or ErrorCode.RevisionConflict or ErrorCode.DuplicateEventId => SerializationFailure,
            ErrorCode.UnknownWriteOutcome => OutcomeUnknown,
            ErrorCode.Corruption => DataCorruption,
            ErrorCode.OwnershipLost or ErrorCode.RecoveryRequired => ConnectionFailure,
            _ => InternalFailure
        };
        var message = code.HasValue ? code.Value.ToString() : InternalMessage;
        // Only closed server-authored fields; never Exception.Message, SQL, parameters or credential text.
        var bytes = Encoding.UTF8.GetBytes(SeverityField + ErrorSeverity + CodeField + state + MessageField + message + EndFields);
        writer.Write(PostgresWireWriter.ErrorResponse, bytes);
    }

    internal const string InvalidAuthorization = "28P01";
    internal const string InsufficientPrivilege = "42501";
    internal const string InvalidParameter = "22023";
    internal const string FeatureUnsupported = "0A000";
    internal const string QueryCancelled = "57014";
    internal const string ResourcesExhausted = "54000";
    internal const string SerializationFailure = "40001";
    internal const string OutcomeUnknown = "08007";
    internal const string ConnectionFailure = "08006";
    internal const string DataCorruption = "XX001";
    internal const string InternalFailure = "XX000";
    private const string SeverityField = "S";
    private const string CodeField = "\0C";
    private const string MessageField = "\0M";
    private const string EndFields = "\0\0";
    private const string ErrorSeverity = "ERROR";
    private const string InternalMessage = "OperationFailed";
}
