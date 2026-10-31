namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

/// <summary>One node-count line of a comparison cell with its status, exact recorded value and visible text.</summary>
internal sealed record SiteTableLine(int Nodes, string Status, string Value, string Text);
