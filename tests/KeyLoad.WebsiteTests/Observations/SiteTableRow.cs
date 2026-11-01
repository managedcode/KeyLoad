namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

/// <summary>One database row of a comparison table: the note beside its name and its lines per case.</summary>
internal sealed record SiteTableRow(string Database, string Type, string Note, IReadOnlyDictionary<string, SiteTableLine[]> Cells);
