using SessionFields = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.BenchmarkSessionEvidenceKeys;
namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Verifies digest-backed normal-host provenance failures through the real CLI process.</summary>
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
    public async Task AcImage002MissingKeyLoadDigestReferenceNamesOnlyItsSetting()
    {
        var settings = ComparisonExecutionIdentitySupport.ValidSettings();
        settings.Remove(ComparisonExecutionIdentitySupport.KeyLoadImage);
        var result = await ComparisonExecutionIdentitySupport.RunAsync(settings);

        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentitySupport.MissingKeyPrefix + "Benchmarks:Images:KeyLoad");
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
    public async Task AcImage002MalformedLoadGeneratorDigestFailsWithoutEchoingReference()
    {
        var settings = ComparisonExecutionIdentitySupport.ValidSettings();
        settings[ComparisonExecutionIdentitySupport.LoadGeneratorImage] =
            "registry.invalid/private-path?token=" + ComparisonExecutionIdentitySupport.PrivateCanary;
        var result = await ComparisonExecutionIdentitySupport.RunAsync(settings);

        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentitySupport.InvalidCode, ComparisonExecutionIdentitySupport.PrivateCanary);
    }

    [Test]
    public async Task AcImage002MalformedKeyLoadDigestFailsWithoutEchoingReference()
    {
        var settings = ComparisonExecutionIdentitySupport.ValidSettings();
        settings[ComparisonExecutionIdentitySupport.KeyLoadImage] =
            "registry.invalid/private-path?token=" + ComparisonExecutionIdentitySupport.PrivateCanary;
        var result = await ComparisonExecutionIdentitySupport.RunAsync(settings);

        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentitySupport.InvalidCode, ComparisonExecutionIdentitySupport.PrivateCanary);
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
    public async Task AcImage005IdentityIsValidatedBeforeNativeClientEndpoints()
    {
        var settings = ComparisonExecutionIdentitySupport.ValidSettings();
        settings.Remove(ComparisonExecutionIdentitySupport.LoadGeneratorImage);
        settings[SessionFields.FirstEndpointEnvironment] = ComparisonExecutionIdentitySupport.PrivateCanary;
        var result = await ComparisonExecutionIdentitySupport.RunAsync(settings);
        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentitySupport.MissingKeyPrefix + "Benchmarks:LoadGeneratorImage",
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
