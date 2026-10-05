namespace KeyLoad.Comparisons;

/// <summary>Admits the exact closed set of scaled comparison profile identifiers.</summary>
public static class ScaledComparisonProfileParser
{
    private const string Scale100k = "scaled-100k-c16";
    private const string Scale1m = "scaled-1m-c16";

    /// <summary>Parses one exact accepted profile identifier.</summary>
    /// <param name="id">Closed profile identifier.</param>
    /// <returns>The immutable bounded profile.</returns>
    public static ScaledComparisonProfile Parse(string id) => id switch
    {
        Scale100k => new(id, ScaledComparisonProfileParserValues.Scale100kCount),
        Scale1m => new(id, ScaledComparisonProfileParserValues.Scale1mCount),
        _ => throw new ArgumentOutOfRangeException(nameof(id), ScaledComparisonProfileParserValues.UnknownScaledComparisonProfile)
    };
}
