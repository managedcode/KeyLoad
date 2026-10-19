namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class BenchmarkSessionParserTests
{
    // AC-ISO-006: genuine driver URI parser errors never print connection credentials.
    [Test]
    [Arguments("MongoDB", "mongodb://private-isolated-host-canary/?tls=invalid")]
    [Arguments("KurrentDB", "esdb://private-isolated-host-canary/?tls=invalid")]
    public async Task ConstructorFailureHasFixedSafeOutput(string target, string connection)
    {
        using var fixture = new BenchmarkSessionStartupFixture();
        var settings = fixture.Settings(target, 1);
        settings[BenchmarkSessionStartupFixture.Connection] = connection;
        settings[BenchmarkSessionStartupFixture.EndpointPrefix + "0"] = "http://127.0.0.1:1/";
        await fixture.AssertFailureAsync(await BenchmarkSessionStartupFixture.RunAsync(settings), BenchmarkSessionStartupFixture.Failure);
    }
}
