namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Exercises invalid immutable image references through the normal real CLI process.</summary>
internal sealed class ComparisonExecutionIdentityReferenceTests
{
    private static string Digest => "sha256:" + ComparisonExecutionIdentitySupport.ImageDigest;

    [Test]
    public async Task AcImage002RegistryPortMustBeNumeric()
    {
        var result = await RunAsync("registry.invalid:badport/keyload/server:tag@" + Digest);

        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            "session");
    }

    [Test]
    public async Task AcImage002RegistryPortMustBeInRange()
    {
        var result = await RunAsync("registry.invalid:70000/keyload/server:tag@" + Digest);

        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            "session");
    }

    [Test]
    public async Task AcImage002ImageReferenceCannotContainEmptyPathSegments()
    {
        var result = await RunAsync("registry.invalid//keyload/server:tag@" + Digest);

        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            "session");
    }

    [Test]
    public async Task AcImage002ImageReferenceCannotUseMultipleRegistryPortSeparators()
    {
        var result = await RunAsync("registry.invalid:5000:6000/keyload/server:tag@" + Digest);

        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            "session");
    }

    [Test]
    public async Task AcImage002ImageReferenceMustStayWithinTheParserBound()
    {
        var oversizedRegistry = new string('a', 1_025);
        var result = await RunAsync(oversizedRegistry + "/keyload/server:tag@" + Digest);

        await ComparisonExecutionIdentitySupport.AssertSafeFailureAsync(result,
            "session");
    }

    private static Task<ComparisonHostExit> RunAsync(string image)
    {
        var settings = ComparisonExecutionIdentitySupport.ValidSettings();
        settings[SessionHostStartupSupport.Target] = "RabbitMQ";
        settings[SessionHostStartupSupport.Image] = image;
        return ComparisonExecutionIdentitySupport.RunAsync(settings);
    }
}
