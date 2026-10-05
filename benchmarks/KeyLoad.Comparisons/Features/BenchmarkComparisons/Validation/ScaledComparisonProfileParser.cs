namespace KeyLoad.Comparisons;

/// <summary>Admits the exact closed set of scaled comparison profile identifiers.</summary>
public static class ScaledComparisonProfileParser
{
    /// <summary>Parses one exact accepted profile identifier.</summary>
    /// <param name="id">Closed profile identifier.</param>
    /// <returns>The immutable bounded profile.</returns>
    public static ScaledComparisonProfile Parse(string id) => id switch
    {
        "scaled-100k-c16" => new(id, 100_000),
        "scaled-1m-c16" => new(id, 1_000_000),
        "scaled-5m-c16" => new(id, 5_000_000),
        _ => throw new ArgumentOutOfRangeException(nameof(id), "Unknown scaled comparison profile.")
    };
}
