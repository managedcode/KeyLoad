namespace KeyLoad;

/// <summary>Validates bounded native offline slot identity without creating database authority.</summary>
public static class ClusterRestoreSlotContextValidation
{
    private const char FirstHexDigit = '0';
    private const char LastHexDigit = '9';
    private const char FirstHexLetter = 'a';
    private const char LastHexLetter = 'f';
    private const int FirstOrdinal = 0;
    private const long FirstPosition = 0;
    private const int DigestCharacters = 64;
    private const string Invalid = "The native restore slot context is incomplete or noncanonical.";

    /// <summary>Requires stable operation, source and fresh target identities with exact native SHA256 text.</summary>
    /// <param name="context">Separately admitted original operation context.</param>
    public static void Require(ClusterRestoreSlotContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Version != ClusterRestoreSlotContext.CurrentVersion || context.OperationId == Guid.Empty
            || context.SlotOrdinal < FirstOrdinal || context.SourcePosition < FirstPosition
            || context.SourceNodeId == Guid.Empty || context.SourceIncarnation == Guid.Empty
            || context.TargetNodeId == Guid.Empty || context.TargetIncarnation == Guid.Empty
            || context.TargetNodeId == context.SourceNodeId || context.TargetIncarnation == context.SourceIncarnation
            || !Digest(context.PlanDigest) || !Digest(context.SourceEnvelopeDigest) || !Digest(context.TargetSignerFingerprint))
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
    }

    private static bool Digest(string value)
    {
        if (value is null || value.Length != DigestCharacters)
        { return false; }
        foreach (var character in value)
        { if (character is not (>= FirstHexDigit and <= LastHexDigit) and not (>= FirstHexLetter and <= LastHexLetter)) { return false; } }
        return true;
    }
}
