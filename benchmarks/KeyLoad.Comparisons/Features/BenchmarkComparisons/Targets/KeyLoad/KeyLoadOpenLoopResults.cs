namespace KeyLoad.Comparisons.Targets;

internal static class KeyLoadOpenLoopResults
{
    internal static OpenLoopSessionResult RejectOrThrow(string? errorCode)
    {
        if (errorCode == nameof(ErrorCode.ResourceExhausted))
        {
            return new(OpenLoopSessionDisposition.TargetRejected);
        }
        throw new ComparisonFailureException(OpenLoopProtocolIdentities.KeyLoadFailurePrefix + errorCode);
    }
}
