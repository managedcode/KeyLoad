namespace KeyLoad.Orleans;

internal static class ReplicaMembershipAuthorityText
{
    private const string InvalidRequest = "The membership authority request is invalid.";
    private const string Unauthenticated = "The membership authority request is unauthenticated.";

    private const string UnsupportedProtocol = "The membership authority protocol is not supported.";
    private const string CorruptTable = "The persisted membership table is corrupt.";
    private const string OperationFailed = "The membership authority operation failed.";

    internal const string Unavailable = "The membership authority is unavailable.";
    internal const string InvalidSignature = "The membership authority response authentication is invalid.";
    internal const string InvalidReply = "The membership authority response is invalid.";
    internal const string ReplyTooLarge = "The membership authority reply is too large.";
    internal const string InvalidOptions = "The membership authority client settings are invalid.";
    internal const string DeleteUnsupported = "Remote membership table deletion is unsupported.";
    internal const string InvalidIdentity = "The membership authority identity is invalid.";
    internal const string MembershipCapacity = "The membership authority capacity is exhausted.";

    internal static string For(ErrorCode code) => code switch
    {
        ErrorCode.Validation => InvalidRequest,
        ErrorCode.Unauthenticated => Unauthenticated,
        ErrorCode.UnsupportedCapability => UnsupportedProtocol,
        ErrorCode.ResourceExhausted => MembershipCapacity,
        ErrorCode.Corruption => CorruptTable,
        ErrorCode.OwnershipLost => Unavailable,
        _ => OperationFailed
    };
}
