using System.Globalization;
using SessionFields = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.BenchmarkSessionEvidenceKeys;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Runs real shared-session admission with exclusively owned native configuration evidence.</summary>
internal sealed class BenchmarkSessionStartupFixture : IDisposable
{
    internal const string Failure = "IsolatedComparisonHostFailed";
    internal const string Canary = "private-isolated-host-canary";
    internal const string Image = "Benchmarks__Native__Image";
    internal const string EndpointPrefix = "Benchmarks__Native__Endpoints__";
    internal const string Endpoints = "Benchmarks__Native__Endpoints";
    internal const string Connection = "Benchmarks__Native__ConnectionString";
    internal const string AdminKey = "Benchmarks__AdminKey";
    internal const string User = "Benchmarks__Native__User";
    internal const string Password = "Benchmarks__Native__Password";
    private const string NativePrefix = "Benchmarks__Native__";
    private readonly ImageBundleTestDirectory directory = new();

    internal Dictionary<string, string> Settings(string target = "KeyLoad", int nodes = 3)
    {
        var settings = SessionHostStartupSupport.ValidSettings();
        settings[SessionHostStartupSupport.Target] = target;
        settings[SessionHostStartupSupport.Nodes] = nodes.ToString(CultureInfo.InvariantCulture);
        settings[SessionFields.OutputEnvironment] = directory.Root;
        foreach (var key in settings.Keys.Where(key => key.StartsWith(NativePrefix, StringComparison.Ordinal)
            && key != Image).ToArray())
        {
            settings.Remove(key);
        }
        settings.Remove(AdminKey);
        return settings;
    }

    internal static Task<ComparisonHostExit> RunAsync(Dictionary<string, string> settings)
        => ComparisonHostProcess.RunAsync([], settings, TestContext.Current!.Execution.CancellationToken);

    internal async Task AssertFailureAsync(ComparisonHostExit result, string? expectedDetail = null)
    {
        var text = result.Stdout + result.Stderr;
        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(text).Contains(SessionHostStartupSupport.NativeNames[0]);
        if (expectedDetail is not null)
        {
            await Assert.That(text).Contains(expectedDetail);
        }
        await Assert.That(text.Contains(Canary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(text.Contains(SessionHostStartupSupport.Canary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(File.Exists(Path.Combine(directory.Root, "reports", "load-million.json"))).IsFalse();
    }

    public void Dispose() => directory.Dispose();
}
