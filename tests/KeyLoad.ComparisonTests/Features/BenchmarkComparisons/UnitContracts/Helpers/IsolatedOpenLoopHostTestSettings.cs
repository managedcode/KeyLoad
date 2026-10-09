namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Creates independent fixed inputs for actual open-loop host process cases.</summary>
internal static class IsolatedOpenLoopHostTestSettings
{
    internal const string ProfileId = "scaled-100k-c16";
    internal const string RateKey = "Benchmarks__OpenLoopRate";
    internal const string ProofKey = "Benchmarks__OpenLoopCancellationProof";
    private const string ScaleProfileKey = "Benchmarks__ScaleProfile";
    private const string OpenLoopOptionsPrefix = "Benchmarks__OpenLoopExecution__";
    private const string OfferedRate = "250";

    internal static Dictionary<string, string> Create(IsolatedHostFixture fixture)
    {
        var settings = fixture.Settings("Neo4j", 3);
        settings[ScaleProfileKey] = ProfileId;
        settings[ComparisonExecutionIdentitySupport.EvidenceProfile] = ProfileId;
        settings[RateKey] = OfferedRate;
        return settings;
    }

    internal static string OptionKey(string option) => OpenLoopOptionsPrefix + option;

    internal static void AddNativeCanaries(Dictionary<string, string> settings)
    {
        settings[IsolatedHostFixture.Connection] = IsolatedHostFixture.Canary;
        settings[IsolatedHostFixture.EndpointPrefix + "0"] = "not-a-uri-" + IsolatedHostFixture.Canary;
    }
}
