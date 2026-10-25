namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

internal sealed record SiteIsolatedSelection(string Scenario, int NodeCount, object Repetition, string Metric, string Target);
