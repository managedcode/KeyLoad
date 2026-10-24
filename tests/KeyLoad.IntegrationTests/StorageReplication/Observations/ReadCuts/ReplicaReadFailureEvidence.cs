using System.Globalization;

namespace KeyLoad.IntegrationTests.StorageReplication;

internal static class ReplicaReadFailureEvidence
{
    private const string Leaf = "Replica read barrier rejected:";
    private const string Cause = "Orleans original stream cause:";
    private const string Rpc = "Orleans request stream failed:";
    private const string Stage = "; Stage=";
    private const string Flags = "; Flags=";
    private const string Period = ".";
    private const string Join = " | ";
    private const string GuidFormat = "D";
    private const int GuidCharacters = 36;
    private const int LastStage = 8;
    private const int MaximumFlags = 7;

    internal static string Attach(string failure, IReadOnlyList<string> tail)
    {
        if (!Request(failure, Cause, out var id) && !Request(failure, Rpc, out id))
        {
            return failure;
        }
        foreach (var line in tail.Reverse())
        {
            if (Read(line, out var candidate, out var stage, out var flags) && candidate == id)
            {
                return failure + Join + string.Create(CultureInfo.InvariantCulture,
                    $"{Leaf} {id}; Stage={stage}; Flags={flags}.");
            }
        }
        return failure;
    }

    private static bool Request(string line, string marker, out Guid id)
    {
        id = default;
        var start = line.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }
        var value = line.AsSpan(start + marker.Length).TrimStart();
        return value.Length >= GuidCharacters && Guid.TryParseExact(value[..GuidCharacters], GuidFormat, out id)
            && id != Guid.Empty;
    }

    private static bool Read(string line, out Guid id, out int stage, out int flags)
    {
        stage = flags = default;
        if (!Request(line, Leaf, out id))
        {
            return false;
        }
        var start = line.IndexOf(Leaf, StringComparison.Ordinal);
        var value = line.AsSpan(start + Leaf.Length).TrimStart()[GuidCharacters..];
        var split = value.IndexOf(Flags, StringComparison.Ordinal);
        if (!value.StartsWith(Stage, StringComparison.Ordinal) || split < Stage.Length
            || !value.EndsWith(Period, StringComparison.Ordinal))
        {
            return false;
        }
        return int.TryParse(value[Stage.Length..split], NumberStyles.None, CultureInfo.InvariantCulture, out stage)
            && int.TryParse(value[(split + Flags.Length)..^Period.Length], NumberStyles.None,
                CultureInfo.InvariantCulture, out flags)
            && stage >= 1 && stage <= LastStage && flags >= 0 && flags <= MaximumFlags;
    }
}
