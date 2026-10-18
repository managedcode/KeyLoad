namespace KeyLoad.Comparisons;

/// <summary>A document returned by a comparison target.</summary>
/// <param name="Id">The returned document identifier.</param>
/// <param name="Json">The returned serialized document payload.</param>
public sealed record FoundDocument(string Id, string Json);
