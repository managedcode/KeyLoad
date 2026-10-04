namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedHostNativeParserTests
{
    // AC-ISO-006: genuine driver URI parser errors never print connection credentials.
    [Test]
    [Arguments("MongoDB", "mongodb://private-isolated-host-canary/?tls=invalid")]
    [Arguments("KurrentDB", "esdb://private-isolated-host-canary/?tls=invalid")]
    public async Task NativeConstructorFailureHasFixedSafeOutput(string target, string connection)
    {
        using var fixture = new IsolatedHostFixture();
        var settings = fixture.Settings(target, 1);
        settings[IsolatedHostFixture.Connection] = connection;
        settings[IsolatedHostFixture.EndpointPrefix + "0"] = "http://127.0.0.1:1/";
        await fixture.AssertFailureAsync(await IsolatedHostFixture.RunAsync(settings));
    }
}
