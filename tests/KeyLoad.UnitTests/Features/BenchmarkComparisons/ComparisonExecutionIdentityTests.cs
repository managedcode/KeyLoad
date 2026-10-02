namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Verifies digest-backed normal-host provenance failures through the real CLI process.</summary>
internal sealed class ComparisonExecutionIdentityTests
{
    private const string SingleProfile = "smoke-single";
    private const string ReplicatedProfile = "smoke-replicated";

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
    public async Task AcImage005SingleEvidenceProfileCannotDescribeReplicatedTopology()
    {
        var settings = ComparisonExecutionIdentitySupport.ValidSettings(SingleProfile);
        settings[ComparisonExecutionIdentitySupport.Topology] = "Replicated";
        var result = await ComparisonExecutionIdentitySupport.RunAsync(settings);

        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            ComparisonExecutionIdentitySupport.InvalidCode, ReplicatedProfile);
    }

    [Test]
    public async Task AcImage005ExistingNormalSettingFailurePrecedesIdentityFailure()
    {
        var settings = ComparisonExecutionIdentitySupport.ValidSettings();
        settings.Remove(ComparisonHostBindingsSupport.QdrantEndpoint);
        settings[ComparisonExecutionIdentitySupport.LoadGeneratorImage] =
            "registry.invalid/private-path?token=" + ComparisonExecutionIdentitySupport.PrivateCanary;
        var result = await ComparisonExecutionIdentitySupport.RunAsync(settings);

        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            ComparisonHostBindingsSupport.MissingSettingPrefix + "Benchmarks:QdrantEndpoint",
            ComparisonExecutionIdentitySupport.PrivateCanary);
    }
}
