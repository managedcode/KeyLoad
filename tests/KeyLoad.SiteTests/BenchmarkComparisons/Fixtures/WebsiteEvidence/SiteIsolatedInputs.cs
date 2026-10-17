namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteIsolatedInputs(SiteTestInputs Site, string Aggregate)
{
    public const string AggregateEnvironment = "KEYLOAD_SITE_ISOLATED_AGGREGATE";
    public const string MissingEvidence = "Isolated site tests require the authentic complete GitHub aggregate.";

    public static SiteIsolatedInputs Read()
    {
        var input = Environment.GetEnvironmentVariable(AggregateEnvironment);
        if (string.IsNullOrWhiteSpace(input) || !Path.IsPathFullyQualified(input) ||
            !File.Exists(Path.Combine(input, "aggregate.json")))
        {
            throw new InvalidOperationException(MissingEvidence);
        }

        return new(SiteTestInputs.Read(), Path.GetFullPath(input));
    }
}
