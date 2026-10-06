using ManagedCode.Communication;

namespace KeyLoad.Comparisons.Targets;

internal static class KeyLoadClientResults
{
    internal static T Success<T>(Result<T> result, string? stage = null)
    {
        const string KeyLoadDetail = "KeyLoad:";
        const string EmptyText = "";
        const string FieldSeparator = ":";

        if (!result.IsSuccess)
        {
            throw new ComparisonFailureException(KeyLoadDetail + result.Problem?.ErrorCode +
                (stage is null ? EmptyText : FieldSeparator + stage));
        }

        return result.Value!;
    }
}
