using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Checks complete output and no-output outcomes from the native comparison host.</summary>
internal static class IsolatedOpenLoopHostAssertions
{
    private const string UnsupportedReason = "Neo4j Community does not provide native clustering; Enterprise licensing is excluded.";
    private const string WorkerFile = "worker.json";

    internal static async Task AssertNoOutputAsync(IsolatedHostFixture fixture, ComparisonHostExit exit)
    {
        await fixture.AssertFailureAsync(exit);
        await Assert.That((exit.Stdout + exit.Stderr).Contains(IsolatedHostFixture.Canary,
            StringComparison.Ordinal)).IsFalse();
        await Assert.That(Directory.Exists(fixture.DirectoryPath)).IsFalse();
    }

    internal static async Task AssertUnsupportedWorkerAsync(IsolatedHostFixture fixture, ComparisonHostExit exit)
    {
        await Assert.That(exit.ExitCode).IsEqualTo(0);
        await Assert.That(exit.Stderr).IsEqualTo(string.Empty);
        await Assert.That((exit.Stdout + exit.Stderr).Contains(IsolatedHostFixture.Canary,
            StringComparison.Ordinal)).IsFalse();
        var report = await ReadAsync(fixture);
        await Assert.That(report.SchemaVersion).IsEqualTo(5);
        await Assert.That(report.Disposition).IsEqualTo("unsupportedTopology");
        await Assert.That(report.Reason).IsEqualTo(UnsupportedReason);
        await Assert.That(report.Report).IsNull();
        await Assert.That(report.Worker).IsEqualTo(ExpectedWorker());
        await Assert.That(Directory.GetFiles(fixture.DirectoryPath).Select(Path.GetFileName)
            .SequenceEqual([WorkerFile])).IsTrue();
    }

    private static IsolatedComparisonWorker ExpectedWorker()
        => new("Neo4j", 2, Scenario.PointRead, IsolatedOpenLoopHostTestSettings.ProfileId,
            ComparisonExecutionIdentitySupport.Revision, 37070000000, 2, "managedcode/KeyLoad",
            "refs/heads/main", "CI", 111047630080);

    private static async Task<IsolatedComparisonReport> ReadAsync(IsolatedHostFixture fixture)
    {
        await using var stream = File.OpenRead(Path.Combine(fixture.DirectoryPath, WorkerFile));
        return await JsonSerializer.DeserializeAsync<IsolatedComparisonReport>(stream, ReportWriter.JsonOptions)
            ?? throw new InvalidOperationException("The unsupported worker envelope is missing.");
    }
}
