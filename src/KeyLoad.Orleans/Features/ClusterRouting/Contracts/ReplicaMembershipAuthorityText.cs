namespace KeyLoad.Orleans;

internal static class ReplicaMembershipAuthorityText
{
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
        ErrorCode.Validation => "The membership authority request is invalid.",
        ErrorCode.Unauthenticated => "The membership authority request is unauthenticated.",
        ErrorCode.UnsupportedCapability => "The membership authority protocol is not supported.",
        ErrorCode.ResourceExhausted => "The membership authority capacity is exhausted.",
        ErrorCode.Corruption => "The persisted membership table is corrupt.",
        ErrorCode.OwnershipLost => Unavailable,
        _ => "The membership authority operation failed."
    };
}
