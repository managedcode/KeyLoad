namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Verifies separate TimeSeries provenance failures through the real CLI process.</summary>
internal sealed class ComparisonExecutionIdentityTimeSeriesTests
{
    [Test]
    public async Task AcImage005MissingTimeSeriesEvidenceProfileNamesOnlyItsSetting()
    {
        var settings = ComparisonExecutionIdentityTimeSeriesSupport.ValidSettings();
        settings.Remove(ComparisonExecutionIdentityTimeSeriesSupport.EvidenceProfile);
        var result = await ComparisonExecutionIdentityTimeSeriesSupport.RunAsync(settings);

        await ComparisonExecutionIdentityTimeSeriesSupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentityTimeSeriesSupport.MissingPrefix + "Benchmarks:EvidenceProfile");
    }

    [Test]
    public async Task AcImage005NormalProfileCannotDescribeTimeSeriesExecution()
    {
        var settings = ComparisonExecutionIdentityTimeSeriesSupport.ValidSettings("smoke-single");
        var result = await ComparisonExecutionIdentityTimeSeriesSupport.RunAsync(settings);

        await ComparisonExecutionIdentityTimeSeriesSupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentityTimeSeriesSupport.InvalidCode);
    }

    [Test]
    public async Task AcImage005TimeSeriesRejectsMismatchedGitHubSha()
    {
        var settings = ComparisonExecutionIdentityTimeSeriesSupport.ValidSettings();
        settings[ComparisonExecutionIdentityTimeSeriesSupport.GitHubSha] =
            ComparisonExecutionIdentitySupport.OtherRevision;
        var result = await ComparisonExecutionIdentityTimeSeriesSupport.RunAsync(settings);

        await ComparisonExecutionIdentityTimeSeriesSupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentityTimeSeriesSupport.InvalidCode,
            ComparisonExecutionIdentitySupport.OtherRevision);
    }

    [Test]
    public async Task AcImage005MalformedTimeSeriesRunnerReferenceOmitsPrivateCanary()
    {
        var settings = ComparisonExecutionIdentityTimeSeriesSupport.ValidSettings();
        settings[ComparisonExecutionIdentityTimeSeriesSupport.LoadGeneratorImage] =
            "registry.invalid:private-port/keyload/runner:tag@sha256="
            + ComparisonExecutionIdentitySupport.PrivateCanary;
        var result = await ComparisonExecutionIdentityTimeSeriesSupport.RunAsync(settings);

        await ComparisonExecutionIdentityTimeSeriesSupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentityTimeSeriesSupport.InvalidCode,
            ComparisonExecutionIdentitySupport.PrivateCanary);
    }

    [Test]
    public async Task AcImage005ExistingTimeSeriesSettingFailurePrecedesIdentityFailure()
    {
        var settings = ComparisonExecutionIdentityTimeSeriesSupport.ValidSettings();
        settings.Remove(ComparisonExecutionIdentityTimeSeriesSupport.TimescaleImage);
        settings[ComparisonExecutionIdentityTimeSeriesSupport.LoadGeneratorImage] =
            "registry.invalid:private-port/keyload/runner:tag@sha256="
            + ComparisonExecutionIdentitySupport.PrivateCanary;
        var result = await ComparisonExecutionIdentityTimeSeriesSupport.RunAsync(settings);

        await ComparisonExecutionIdentityTimeSeriesSupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentityTimeSeriesSupport.MissingPrefix + "Benchmarks:Images:Timescale",
            ComparisonExecutionIdentitySupport.PrivateCanary);
    }
}
