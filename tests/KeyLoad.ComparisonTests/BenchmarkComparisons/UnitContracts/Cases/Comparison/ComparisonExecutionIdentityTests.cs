using SessionFields = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.BenchmarkSessionEvidenceKeys;
namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Verifies original workflow provenance failures through the real execution client process.</summary>
internal sealed class ComparisonExecutionIdentityTests
{
    private const string SingleProfile = "smoke-single";

    [Test]
    public async Task AcImage005MissingEvidenceProfileNamesOnlyItsSetting()
    {
        var settings = ComparisonExecutionIdentitySupport.ValidSettings();
        settings.Remove(ComparisonExecutionIdentitySupport.EvidenceProfile);
        var result = await ComparisonExecutionIdentitySupport.RunAsync(settings);

        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentitySupport.MissingKeyPrefix + "Benchmarks:EvidenceProfile");
    }

    [Test]
    public async Task AcImage005NonPositiveGitHubRunIdFailsWithSafeCode()
    {
        var settings = ComparisonExecutionIdentitySupport.ValidSettings();
        settings[ComparisonExecutionIdentitySupport.RunId] = "0";
        var result = await ComparisonExecutionIdentitySupport.RunAsync(settings);

        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentitySupport.InvalidCode);
    }

    [Test]
    public async Task AcImage005MismatchedSourceRevisionFailsWithoutEchoingRevision()
    {
        var settings = ComparisonExecutionIdentitySupport.ValidSettings();
        settings[ComparisonExecutionIdentitySupport.SourceRevision] = ComparisonExecutionIdentitySupport.OtherRevision;
        var result = await ComparisonExecutionIdentitySupport.RunAsync(settings);

        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentitySupport.InvalidCode, ComparisonExecutionIdentitySupport.OtherRevision);
    }

    [Test]
    public async Task AcImage005UppercaseSourceRevisionFailsWithoutEchoingRevision()
    {
        var settings = ComparisonExecutionIdentitySupport.ValidSettings();
        var uppercaseRevision = ComparisonExecutionIdentitySupport.Revision.ToUpperInvariant();
        settings[ComparisonExecutionIdentitySupport.SourceRevision] = uppercaseRevision;
        settings[ComparisonExecutionIdentitySupport.GitHubSha] = uppercaseRevision;
        var result = await ComparisonExecutionIdentitySupport.RunAsync(settings);

        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentitySupport.InvalidCode, uppercaseRevision);
    }

    [Test]
    public async Task AcImage005MismatchedGitHubShaFailsWithoutEchoingSha()
    {
        var settings = ComparisonExecutionIdentitySupport.ValidSettings();
        settings[ComparisonExecutionIdentitySupport.GitHubSha] = ComparisonExecutionIdentitySupport.OtherRevision;
        var result = await ComparisonExecutionIdentitySupport.RunAsync(settings);

        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentitySupport.InvalidCode, ComparisonExecutionIdentitySupport.OtherRevision);
    }

    [Test]
    public async Task AcImage005UnknownEvidenceProfileFailsWithoutEchoingProfile()
    {
        var settings = ComparisonExecutionIdentitySupport.ValidSettings();
        settings[ComparisonExecutionIdentitySupport.EvidenceProfile] = ComparisonExecutionIdentitySupport.PrivateCanary;
        var result = await ComparisonExecutionIdentitySupport.RunAsync(settings);

        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentitySupport.InvalidCode, ComparisonExecutionIdentitySupport.PrivateCanary);
    }

    [Test]
    public async Task AcImage005SupersededSingleCellProfileCannotDescribeSharedSession()
    {
        var settings = ComparisonExecutionIdentitySupport.ValidSettings(SingleProfile);
        var result = await ComparisonExecutionIdentitySupport.RunAsync(settings);

        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentitySupport.InvalidCode, SingleProfile);
    }

    [Test]
    public async Task AcImage005IdentityIsValidatedBeforeClientEndpoints()
    {
        var settings = ComparisonExecutionIdentitySupport.ValidSettings();
        settings.Remove(ComparisonExecutionIdentitySupport.RunId);
        settings[SessionFields.FirstEndpointEnvironment] = ComparisonExecutionIdentitySupport.PrivateCanary;
        var result = await ComparisonExecutionIdentitySupport.RunAsync(settings);
        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentitySupport.MissingKeyPrefix + "GITHUB_RUN_ID",
            ComparisonExecutionIdentitySupport.PrivateCanary);
    }
    [Test]
    [Arguments(ComparisonExecutionIdentitySupport.Repository, "external/fork")]
    [Arguments(ComparisonExecutionIdentitySupport.GitHubRef, "refs/heads/other")]
    [Arguments(ComparisonExecutionIdentitySupport.Workflow, "Other workflow")]
    public async Task SessionRequiresCanonicalRepositoryMainAndWorkflow(string key, string value)
    {
        var settings = ComparisonExecutionIdentitySupport.ValidSettings();
        settings[key] = value;
        var result = await ComparisonExecutionIdentitySupport.RunAsync(settings);
        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentitySupport.InvalidCode);
    }

}
