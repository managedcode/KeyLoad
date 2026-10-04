namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Exercises RF3 and Rabbit caller-binding validation in the real host process.</summary>
internal sealed class ComparisonHostBindingsTests
{
    private const string PrimaryMismatch = "http://127.0.0.1:11";
    private const string DuplicatePeer = "http://127.0.0.1:2";
    private const string MalformedPeer = "not-an-absolute-uri";
    private const string NonHttpPeer = "ftp://127.0.0.1:12";
    private const string UserInfoPeer = "http://user:bindings-url-sentinel@127.0.0.1:13";
    private const string QueryPeer = ComparisonHostBindingsSupport.InvalidEndpointValue;
    private const string FragmentPeer = "http://127.0.0.1:15/#private";
    private const string ExtraPeerIndex = "3";

    [Test]
    public async Task AcPerf002MissingPeerNamesOnlyTheMissingSettingAfterQdrant()
    {
        var settings = ComparisonHostBindingsSupport.ValidSettings();
        settings.Remove(ComparisonHostBindingsSupport.PeerPrefix + ComparisonHostBindingsSupport.PeerTwoIndex);
        var result = await RunAsync(settings);

        await ComparisonHostBindingsSupport.AssertSafeFailureAsync(result,
            ComparisonHostBindingsSupport.MissingSettingPrefix + "Benchmarks:KeyLoadEndpoints:2");
    }

    [Test]
    public async Task AcPerf002MalformedPeerFailsWithSafeCode()
    {
        var settings = ComparisonHostBindingsSupport.ValidSettings();
        settings[ComparisonHostBindingsSupport.PeerPrefix + ComparisonHostBindingsSupport.PeerOneIndex] = MalformedPeer;
        await AssertInvalidEndpointsAsync(settings);
    }

    [Test]
    public async Task AcPerf002DuplicatePeerFailsWithSafeCode()
    {
        var settings = ComparisonHostBindingsSupport.ValidSettings();
        settings[ComparisonHostBindingsSupport.PeerPrefix + ComparisonHostBindingsSupport.PeerTwoIndex] = DuplicatePeer;
        await AssertInvalidEndpointsAsync(settings);
    }

    [Test]
    public async Task AcPerf002ExtraPeerFailsWithSafeCode()
    {
        var settings = ComparisonHostBindingsSupport.ValidSettings();
        settings[ComparisonHostBindingsSupport.PeerPrefix + ExtraPeerIndex] = "http://127.0.0.1:16";
        await AssertInvalidEndpointsAsync(settings);
    }

    [Test]
    public async Task AcPerf002PrimaryMismatchFailsWithSafeCode()
    {
        var settings = ComparisonHostBindingsSupport.ValidSettings();
        settings[ComparisonHostBindingsSupport.PeerPrefix + ComparisonHostBindingsSupport.PeerZeroIndex] = PrimaryMismatch;
        await AssertInvalidEndpointsAsync(settings);
    }

    [Test]
    public async Task AcPerf002NonHttpPeerFailsWithSafeCode()
    {
        var settings = ComparisonHostBindingsSupport.ValidSettings();
        settings[ComparisonHostBindingsSupport.PeerPrefix + ComparisonHostBindingsSupport.PeerOneIndex] = NonHttpPeer;
        await AssertInvalidEndpointsAsync(settings);
    }

    [Test]
    public async Task AcPerf002PeerWithUserInfoFailsWithSafeCode()
    {
        var settings = ComparisonHostBindingsSupport.ValidSettings();
        settings[ComparisonHostBindingsSupport.PeerPrefix + ComparisonHostBindingsSupport.PeerOneIndex] = UserInfoPeer;
        await AssertInvalidEndpointsAsync(settings);
    }

    [Test]
    public async Task AcPerf002PeerWithQueryFailsWithSafeCode()
    {
        var settings = ComparisonHostBindingsSupport.ValidSettings();
        settings[ComparisonHostBindingsSupport.PeerPrefix + ComparisonHostBindingsSupport.PeerOneIndex] = QueryPeer;
        await AssertInvalidEndpointsAsync(settings);
    }

    [Test]
    public async Task AcPerf002PeerWithFragmentFailsWithSafeCode()
    {
        var settings = ComparisonHostBindingsSupport.ValidSettings();
        settings[ComparisonHostBindingsSupport.PeerPrefix + ComparisonHostBindingsSupport.PeerOneIndex] = FragmentPeer;
        await AssertInvalidEndpointsAsync(settings);
    }

    [Test]
    public async Task AcPerf003MissingRabbitManagementEndpointNamesOnlyItsSetting()
    {
        var settings = ComparisonHostBindingsSupport.ValidSettings();
        settings.Remove(ComparisonHostBindingsSupport.RabbitManagementEndpoint);
        var result = await RunAsync(settings);

        await ComparisonHostBindingsSupport.AssertSafeFailureAsync(result,
            ComparisonHostBindingsSupport.MissingSettingPrefix + "Benchmarks:RabbitManagementEndpoint");
    }

    [Test]
    public async Task AcPerf003MissingRabbitUserNamesOnlyItsSetting()
    {
        var settings = ComparisonHostBindingsSupport.ValidSettings();
        settings.Remove(ComparisonHostBindingsSupport.RabbitUser);
        var result = await RunAsync(settings);

        await ComparisonHostBindingsSupport.AssertSafeFailureAsync(result,
            ComparisonHostBindingsSupport.MissingSettingPrefix + "Benchmarks:RabbitUser");
    }

    [Test]
    public async Task AcPerf003MissingRabbitPasswordNamesOnlyItsSetting()
    {
        var settings = ComparisonHostBindingsSupport.ValidSettings();
        settings.Remove(ComparisonHostBindingsSupport.RabbitPassword);
        var result = await RunAsync(settings);

        await ComparisonHostBindingsSupport.AssertSafeFailureAsync(result,
            ComparisonHostBindingsSupport.MissingSettingPrefix + "Benchmarks:RabbitPassword");
    }

    [Test]
    public async Task AcPerf003InvalidRabbitManagementEndpointFailsWithoutLeakingValue()
    {
        var settings = ComparisonHostBindingsSupport.ValidSettings();
        settings[ComparisonHostBindingsSupport.RabbitManagementEndpoint] = QueryPeer;
        var result = await RunAsync(settings);

        await ComparisonHostBindingsSupport.AssertSafeFailureAsync(result,
            ComparisonHostBindingsSupport.InvalidRabbitEndpointCode);
    }

    [Test]
    public async Task AcPerf003InvalidRabbitCredentialsFailWithoutLeakingValue()
    {
        var settings = ComparisonHostBindingsSupport.ValidSettings();
        settings[ComparisonHostBindingsSupport.RabbitUser] = "bindings-rabbit-user\nsecret";
        var result = await RunAsync(settings);

        await ComparisonHostBindingsSupport.AssertSafeFailureAsync(result,
            ComparisonHostBindingsSupport.InvalidRabbitCredentialsCode);
    }

    private static async Task AssertInvalidEndpointsAsync(Dictionary<string, string> settings)
    {
        var result = await RunAsync(settings);
        await ComparisonHostBindingsSupport.AssertSafeFailureAsync(result,
            ComparisonHostBindingsSupport.InvalidEndpointsCode);
    }

    private static Task<ComparisonHostExit> RunAsync(Dictionary<string, string> settings)
        => ComparisonHostProcess.RunAsync([], settings, TestContext.Current!.Execution.CancellationToken);
}
