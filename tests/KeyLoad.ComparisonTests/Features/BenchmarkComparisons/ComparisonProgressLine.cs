using System.Globalization;
using System.Text.RegularExpressions;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Accepts only the bounded public diagnostic marker, never arbitrary resource output.</summary>
internal static partial class ComparisonProgressLine
{
    internal const int MaximumCharacters = 512;
    internal const string FileName = "progress.log";
    private const int MaximumCellCharacters = 128;
    private const int TimestampCharacters = 28;
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private const string InvalidEvidence = "The isolated progress evidence path is invalid.";
    private const string Workers = "workers";
    private const string Failures = "failures";
    private const string MarkerPattern = "\\AKeyLoadBenchmarkProgress phase=(?:oracle|initialize|warmup|prepare|measure|validate|complete)"
        + " repetition=([0-9]{1,10}) completed=([0-9]{1,10}) total=([0-9]{1,10}) failed=([0-9]{1,10})"
        + " elapsedSeconds=([0-9]+(?:\\.[0-9]+)?)\\z";

    internal static bool IsValid(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Length > MaximumCharacters)
        {
            return false;
        }
        var match = Marker().Match(line);
        return match.Success
            && Count(match.Groups[1].ValueSpan, out _)
            && Count(match.Groups[2].ValueSpan, out var completed)
            && Count(match.Groups[3].ValueSpan, out var total)
            && Count(match.Groups[4].ValueSpan, out var failed)
            && failed <= completed && completed <= total
            && double.TryParse(match.Groups[5].ValueSpan, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var elapsed)
            && double.IsFinite(elapsed) && elapsed >= 0;
    }

    internal static string PathForEvidenceDirectory(string evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence);
        var directory = new DirectoryInfo(evidence);
        if (!Path.IsPathFullyQualified(evidence) || directory.Parent is not { Name: Workers } workers
            || workers.Parent is not { } isolated || directory.Name.Length > MaximumCellCharacters
            || !CellName().IsMatch(directory.Name))
        {
            throw new ArgumentException(InvalidEvidence, nameof(evidence));
        }
        return Path.Combine(isolated.FullName, Failures, directory.Name, FileName);
    }

    internal static bool TryFromNativeLog(string line, out string marker)
    {
        marker = string.Empty;
        if (IsValid(line))
        {
            marker = line;
            return true;
        }
        if (line.Length <= TimestampCharacters + 1 || line.Length > MaximumCharacters + TimestampCharacters + 1
            || line[TimestampCharacters] != ' ' || !NativeTimestamp().IsMatch(line[..TimestampCharacters])
            || !DateTimeOffset.TryParseExact(line.AsSpan(0, TimestampCharacters), TimestampFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _))
        {
            return false;
        }
        var body = line[(TimestampCharacters + 1)..];
        if (!IsValid(body))
        {
            return false;
        }
        marker = body;
        return true;
    }

    private static bool Count(ReadOnlySpan<char> value, out int count)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out count);

    [GeneratedRegex(MarkerPattern, RegexOptions.CultureInvariant, 100)]
    private static partial Regex Marker();

    [GeneratedRegex("\\A[a-z0-9]+(?:-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex CellName();

    [GeneratedRegex("\\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{7}Z\\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex NativeTimestamp();
}
