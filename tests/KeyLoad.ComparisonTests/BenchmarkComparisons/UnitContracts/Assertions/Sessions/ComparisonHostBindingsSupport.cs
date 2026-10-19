
namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Supplies current execution session startup and reusable metadata-parser settings.</summary>
internal static class ComparisonHostBindingsSupport
{
    internal const string KeyLoadEndpoint = "Benchmarks__KeyLoadEndpoint";
    internal const string AdminKeySetting = "Benchmarks__AdminKey";
    internal const string OutputSetting = "Benchmarks__Output";
    internal const string StorageSetting = "Benchmarks__Storage";

    internal static Dictionary<string, string> ValidSettings() => SessionHostStartupSupport.ValidSettings();

    internal static async Task AssertSafeFailureAsync(ComparisonHostExit result, string expectedDetail)
    {
        var output = result.Stdout + result.Stderr;
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(output).Contains(expectedDetail);
        await Assert.That(output).Contains(SessionHostStartupSupport.RuntimeNames[0]);
        await Assert.That(output.Contains(SessionHostStartupSupport.Canary, StringComparison.Ordinal)).IsFalse();
    }
}
