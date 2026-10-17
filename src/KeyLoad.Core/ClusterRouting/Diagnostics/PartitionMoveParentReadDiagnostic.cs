namespace KeyLoad.Core;

internal static class PartitionMoveParentReadDiagnostic
{
    internal const string MetadataKey = "KeyLoad.Core.ClusterRouting.ParentReadOperands";
    private const string Unobserved = "unobserved";
    private const string RouteField = "route=";
    private const string ComparisonField = " comparisonObserved=";
    private const string EqualField = " phaseEqual=";
    private const string CanonicalField = " canonicalLength=";
    private const string OriginalField = " originalLength=";
    private const string DifferenceField = " firstDifference=";
    private const int LocalPhase = 1;
    private const int ParentReply = 2;
    private const int Unknown = -1;
    private const int InitialOffset = 0;

    private readonly record struct Witness(int Route, int CanonicalLength, int OriginalLength, int FirstDifference);

    internal static KeyLoadException PhaseMismatch(KeyLoadException error,
        ReadOnlySpan<byte> canonical, ReadOnlySpan<byte> original)
    {
        if (error.Code == ErrorCode.OwnershipLost)
        {
            error.Data[MetadataKey] = new Witness(LocalPhase, canonical.Length, original.Length,
            FirstDifference(canonical, original));
        }
        return error;
    }

    internal static KeyLoadException Propagated(KeyLoadException error)
    {
        if (error.Code == ErrorCode.OwnershipLost)
        {
            error.Data[MetadataKey] = new Witness(ParentReply, Unknown, Unknown, Unknown);
        }
        return error;
    }

    internal static string ReadText(Exception error)
    {
        if (error is not KeyLoadException domain || domain.Code != ErrorCode.OwnershipLost
            || error.Data[MetadataKey] is not Witness witness || !Valid(witness))
        {
            return Unobserved;
        }
        var compared = witness.Route == LocalPhase;
        return FormattableString.Invariant($"{RouteField}{witness.Route}{ComparisonField}{compared}{EqualField}{false}{CanonicalField}{witness.CanonicalLength}{OriginalField}{witness.OriginalLength}{DifferenceField}{witness.FirstDifference}");
    }

    private static bool Valid(Witness witness)
    {
        if (witness.Route == ParentReply)
        {
            return witness.CanonicalLength == Unknown && witness.OriginalLength == Unknown
            && witness.FirstDifference == Unknown;
        }
        return witness.Route == LocalPhase && witness.CanonicalLength >= InitialOffset
            && witness.OriginalLength >= InitialOffset && witness.FirstDifference >= InitialOffset
            && witness.FirstDifference <= Math.Min(witness.CanonicalLength, witness.OriginalLength)
            && witness.FirstDifference < Math.Max(witness.CanonicalLength, witness.OriginalLength);
    }

    private static int FirstDifference(ReadOnlySpan<byte> canonical, ReadOnlySpan<byte> original)
    {
        var length = Math.Min(canonical.Length, original.Length);
        for (var index = InitialOffset; index < length; index++)
        {
            if (canonical[index] != original[index])
            {
                return index;
            }
        }
        return canonical.Length == original.Length ? Unknown : length;
    }
}
