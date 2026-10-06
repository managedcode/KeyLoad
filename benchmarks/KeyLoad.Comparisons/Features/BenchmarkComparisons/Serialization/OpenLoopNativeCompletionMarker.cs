using System.Globalization;

namespace KeyLoad.Comparisons;

/// <summary>Formats and parses the fixed, bounded cancellation milestone line.</summary>
public static class OpenLoopNativeCompletionMarker
{
    private const int PrefixFieldIndex = 0;
    private const int ProfileIdFieldIndex = 1;
    private const int ScenarioFieldIndex = 2;
    private const int RateFieldIndex = 3;
    private const int CompletedFieldIndex = 4;
    private const int StartedFieldIndex = 5;
    private const int PlannedFieldIndex = 6;
    /// <summary>Formats one actual runner progress snapshot using invariant decimal counters.</summary>
    /// <param name="progress">The actual bounded runner snapshot.</param>
    /// <param name="profileId">The exact selected scale profile.</param>
    /// <returns>The bounded ASCII marker.</returns>
    public static string Format(OpenLoopProgressV1 progress, string profileId)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        _ = ScaledComparisonProfileParser.Parse(profileId);
        if (progress.Scenario != Scenario.PointRead)
        {
            throw new ArgumentOutOfRangeException(nameof(progress));
        }
        var marker = string.Join(OpenLoopCancellationProofContract.MarkerFieldSeparator, OpenLoopCancellationProofContract.CompletionMarkerPrefix, profileId,
            progress.Scenario.ToString(), progress.OfferedRatePerSecond.ToString(CultureInfo.InvariantCulture),
            progress.Completed.ToString(CultureInfo.InvariantCulture), progress.Started.ToString(CultureInfo.InvariantCulture),
            progress.Planned.ToString(CultureInfo.InvariantCulture));
        return marker.Length <= OpenLoopCancellationProofContract.MaximumMarkerBytes
            ? marker : throw new ComparisonFailureException(OpenLoopFailureCodes.OpenLoopCancellationMarkerInvalid);
    }

    /// <summary>Parses only the exact bounded marker contract and supported native cell identity.</summary>
    /// <param name="value">The raw captured marker line.</param>
    /// <param name="marker">The parsed typed marker when admitted.</param>
    /// <returns><see langword="true"/> when the exact marker contract is valid.</returns>
    public static bool TryParse(string? value, out OpenLoopNativeCompletionV1? marker)
    {
        const int NoMeasuredRate = 0;

        marker = null;
        if (string.IsNullOrEmpty(value) || value.Length > OpenLoopCancellationProofContract.MaximumMarkerBytes
            || value.Any(character => character > OpenLoopCancellationProofContract.MaximumMarkerAsciiCodePoint))
        {
            return false;
        }
        var fields = value.Split(OpenLoopCancellationProofContract.MarkerFieldSeparator);
        if (fields.Length != OpenLoopCancellationProofContract.CompletionMarkerFieldCount
            || fields[PrefixFieldIndex] != OpenLoopCancellationProofContract.CompletionMarkerPrefix
            || !TryProfile(fields[ProfileIdFieldIndex]) || !Enum.TryParse<Scenario>(fields[ScenarioFieldIndex], false, out var scenario)
            || scenario != Scenario.PointRead || scenario.ToString() != fields[ScenarioFieldIndex]
            || !TryInt32(fields[RateFieldIndex], out var rate) || !OpenLoopRateContract.AcceptedRates.Contains(rate)
            || !TryInt64(fields[CompletedFieldIndex], out var completed) || completed < OpenLoopRateContract.ProgressInterval
            || completed % OpenLoopRateContract.ProgressInterval != NoMeasuredRate
            || !TryInt64(fields[StartedFieldIndex], out var started) || started < completed
            || !TryInt64(fields[PlannedFieldIndex], out var planned) || planned != OpenLoopRateContract.PlannedOperations
            || started > planned)
        {
            return false;
        }
        marker = new(fields[ProfileIdFieldIndex], scenario, rate, completed, started, planned);
        return true;
    }

    private static bool TryProfile(string value)
    {
        try
        { return ScaledComparisonProfileParser.Parse(value).Id == value; }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static bool TryInt32(string value, out int result)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);

    private static bool TryInt64(string value, out long result)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
}
