using ManagedCode.Communication;

namespace KeyLoad.Comparisons.Targets;

internal static class KeyLoadClientResults
{
    internal static T Success<T>(Result<T> result, string? stage = null)
    {
        if (!result.IsSuccess)
        {
            throw new ComparisonFailureException("KeyLoad:" + result.Problem?.ErrorCode +
                (stage is null ? "" : ":" + stage));
        }

        return result.Value!;
    }
}
