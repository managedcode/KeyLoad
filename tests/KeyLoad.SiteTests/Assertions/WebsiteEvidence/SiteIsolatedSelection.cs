namespace KeyLoad.SiteTests.BenchmarkComparisons;

internal sealed record SiteIsolatedSelection(string Scenario, int NodeCount, object Repetition, string Metric, string Target);
