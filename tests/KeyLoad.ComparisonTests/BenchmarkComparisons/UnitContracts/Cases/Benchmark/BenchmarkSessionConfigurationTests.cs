namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-002/006: strict selection, native configuration and trusted identity precede allocation.</summary>
internal sealed class BenchmarkSessionConfigurationTests
{
    [Test]
    [Arguments("KEYLOAD_COMPARISON_JOB_ID", "0")]
    [Arguments("KEYLOAD_COMPARISON_JOB_ID", "111047630080" + BenchmarkSessionStartupFixture.Canary)]
    [Arguments("GITHUB_SHA", "fedcba9876543210fedcba9876543210fedcba98")]
    [Arguments("Benchmarks__NodeCount", "0")]
    [Arguments("Benchmarks__Scenario", "pointread")]
    [Arguments("Benchmarks__EvidenceProfile", "smoke-single")]
    [Arguments("Benchmarks__Output", BenchmarkSessionStartupFixture.Canary)]
    public async Task InvalidIdentityOrSelectionFailsSafelyBeforeOutput(string key, string value)
    {
        using var fixture = new BenchmarkSessionStartupFixture();
        var settings = fixture.Settings();
        settings[key] = value;
        await fixture.AssertFailureAsync(await BenchmarkSessionStartupFixture.RunAsync(settings));
    }

    [Test]
    [Arguments("KEYLOAD_COMPARISON_JOB_ID")]
    [Arguments("GITHUB_RUN_ID")]
    [Arguments("Benchmarks__Output")]
    public async Task MissingRequiredIdentityRejectsBeforeNativeAllocation(string key)
    {
        using var fixture = new BenchmarkSessionStartupFixture();
        var settings = fixture.Settings();
        // An explicit empty value masks any real CI environment inherited by the child.
        settings[key] = string.Empty;
        await fixture.AssertFailureAsync(await BenchmarkSessionStartupFixture.RunAsync(settings));
    }

    [Test]
    [Arguments("KeyLoad")]
    [Arguments("PostgreSQL + pgvector")]
    [Arguments("Qdrant")]
    [Arguments("RabbitMQ")]
    [Arguments("Redis")]
    [Arguments("Neo4j")]
    [Arguments("MongoDB")]
    [Arguments("OpenSearch")]
    [Arguments("KurrentDB")]
    public async Task EachRealTargetRequiresItsOwnNativeSettings(string target)
    {
        using var fixture = new BenchmarkSessionStartupFixture();
        await fixture.AssertFailureAsync(await BenchmarkSessionStartupFixture.RunAsync(fixture.Settings(target, 1)));
    }

    [Test]
    [Arguments("missing")]
    [Arguments("extra")]
    [Arguments("duplicate")]
    [Arguments("credentials")]
    [Arguments("scalar")]
    public async Task KeyLoadEndpointsMustProveExactDistinctNativeCount(string defect)
    {
        using var fixture = new BenchmarkSessionStartupFixture();
        var settings = fixture.Settings("KeyLoad", 3);
        settings[BenchmarkSessionStartupFixture.AdminKey] = BenchmarkSessionStartupFixture.Canary;
        settings[BenchmarkSessionStartupFixture.EndpointPrefix + "0"] = "http://127.0.0.1:1/";
        settings[BenchmarkSessionStartupFixture.EndpointPrefix + "1"] = "http://127.0.0.1:2/";
        settings[BenchmarkSessionStartupFixture.EndpointPrefix + "2"] = "http://127.0.0.1:3/";
        if (defect == "missing")
        {
            settings.Remove(BenchmarkSessionStartupFixture.EndpointPrefix + "1");
        }
        if (defect == "extra")
        {
            settings[BenchmarkSessionStartupFixture.EndpointPrefix + "3"] = "http://127.0.0.1:4/";
        }
        if (defect == "duplicate")
        {
            settings[BenchmarkSessionStartupFixture.EndpointPrefix + "1"] = settings[BenchmarkSessionStartupFixture.EndpointPrefix + "0"];
        }
        if (defect == "credentials")
        {
            settings[BenchmarkSessionStartupFixture.EndpointPrefix + "1"] = "http://" + BenchmarkSessionStartupFixture.Canary + "@localhost/";
        }
        if (defect == "scalar")
        {
            settings[BenchmarkSessionStartupFixture.Endpoints] = BenchmarkSessionStartupFixture.Canary;
        }
        await fixture.AssertFailureAsync(await BenchmarkSessionStartupFixture.RunAsync(settings));
    }

    [Test]
    [Arguments("user")]
    [Arguments("password")]
    [Arguments("newline")]
    public async Task OptionalOpenSearchAuthenticationMustBeOneValidPair(string defect)
    {
        using var fixture = new BenchmarkSessionStartupFixture();
        var settings = fixture.Settings("OpenSearch", 1);
        settings[BenchmarkSessionStartupFixture.EndpointPrefix + "0"] = "http://127.0.0.1:1/";
        if (defect != "password")
        {
            settings[BenchmarkSessionStartupFixture.User] = defect == "newline" ? BenchmarkSessionStartupFixture.Canary + "\n" : BenchmarkSessionStartupFixture.Canary;
        }
        if (defect != "user")
        {
            settings[BenchmarkSessionStartupFixture.Password] = BenchmarkSessionStartupFixture.Canary;
        }
        await fixture.AssertFailureAsync(await BenchmarkSessionStartupFixture.RunAsync(settings));
    }
}
