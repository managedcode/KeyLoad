using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-006: actual unsupported workers retain exact identity and publish immutable JSON atomically.</summary>
internal sealed class IsolatedHostOutputTests
{
    [Test]
    [Arguments(2)]
    [Arguments(3)]
    public async Task UnsupportedCommunityTopologyNeedsNoNativeClientsAndPreservesIdentity(int nodes)
    {
        using var fixture = new IsolatedHostFixture();
        var settings = fixture.Settings(nodes: nodes);
        settings[IsolatedHostFixture.Profile] = "timeseries";
        settings[IsolatedHostFixture.Connection] = IsolatedHostFixture.Canary;
        settings[IsolatedHostFixture.EndpointPrefix + "0"] = "not-a-uri-" + IsolatedHostFixture.Canary;
        var exit = await IsolatedHostFixture.RunAsync(settings);
        await Assert.That(exit.ExitCode).IsEqualTo(0);
        await Assert.That(exit.Stderr).IsEqualTo(string.Empty);
        var report = await ReadAsync(fixture);
        await Assert.That(report.SchemaVersion).IsEqualTo(5);
        await Assert.That(report.Disposition).IsEqualTo("unsupportedTopology");
        await Assert.That(report.Reason).IsEqualTo(IsolatedComparisonContract.Current.UnsupportedTopologies.Single(topology =>
                topology.Target == "Neo4j" && topology.NodeCounts.Contains(nodes)).Reason);
        await Assert.That(report.Report).IsNull();
        await Assert.That(report.Worker).IsEqualTo(new IsolatedComparisonWorker("Neo4j", nodes, Scenario.PointRead,
            "intensive-1k-c16", ComparisonExecutionIdentitySupport.Revision, 37070000000, 2,
            "managedcode/KeyLoad", "refs/heads/main", "CI", 111047630080));
        await Assert.That(Directory.GetFiles(fixture.DirectoryPath).Select(Path.GetFileName).Single()).IsEqualTo("worker.json");
        await Assert.That((exit.Stdout + exit.Stderr).Contains(IsolatedHostFixture.Canary, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ExistingWorkerBytesAreNeverOverwritten()
    {
        using var fixture = new IsolatedHostFixture();
        Directory.CreateDirectory(fixture.DirectoryPath);
        var path = Path.Combine(fixture.DirectoryPath, "worker.json");
        await File.WriteAllTextAsync(path, IsolatedHostFixture.Canary);
        var exit = await IsolatedHostFixture.RunAsync(fixture.Settings());
        await Assert.That(exit.ExitCode).IsEqualTo(2);
        await Assert.That(exit.Stderr.Trim()).IsEqualTo(IsolatedHostFixture.Failure);
        await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(IsolatedHostFixture.Canary);
        await Assert.That(Directory.GetFiles(fixture.DirectoryPath).Length).IsEqualTo(1);
    }

    [Test]
    public async Task ConcurrentWorkersPublishExactlyOneCompleteEnvelope()
    {
        using var fixture = new IsolatedHostFixture();
        var exits = await Task.WhenAll(IsolatedHostFixture.RunAsync(fixture.Settings()), IsolatedHostFixture.RunAsync(fixture.Settings()));
        await Assert.That(exits.Select(item => item.ExitCode).Order().SequenceEqual([0, 2])).IsTrue();
        await Assert.That((await ReadAsync(fixture)).Report).IsNull();
        await Assert.That(Directory.GetFiles(fixture.DirectoryPath).Length).IsEqualTo(1);
        await Assert.That(exits.Single(item => item.ExitCode == 2).Stderr.Trim()).IsEqualTo(IsolatedHostFixture.Failure);
    }

    [Test]
    public async Task BlockedOutputPathFailsSafelyAndPreservesExistingFile()
    {
        using var fixture = new IsolatedHostFixture();
        await File.WriteAllTextAsync(fixture.DirectoryPath, IsolatedHostFixture.Canary);
        var exit = await IsolatedHostFixture.RunAsync(fixture.Settings());
        await Assert.That(exit.ExitCode).IsEqualTo(2);
        await Assert.That(exit.Stderr.Trim()).IsEqualTo(IsolatedHostFixture.Failure);
        await Assert.That(await File.ReadAllTextAsync(fixture.DirectoryPath)).IsEqualTo(IsolatedHostFixture.Canary);
    }

    [Test]
    public async Task UnavailableRealNativeEndpointRetainsFailedReportAndImmutableProvenance()
    {
        using var fixture = new IsolatedHostFixture();
        var settings = fixture.Settings("Neo4j", 1);
        settings[IsolatedHostFixture.EndpointPrefix + "0"] = "http://127.0.0.1:1/";
        settings[IsolatedHostFixture.User] = "neo4j";
        settings[IsolatedHostFixture.Password] = IsolatedHostFixture.Canary;
        var exit = await IsolatedHostFixture.RunAsync(settings);
        await Assert.That(exit.ExitCode).IsEqualTo(2);
        await Assert.That((exit.Stdout + exit.Stderr).Contains(IsolatedHostFixture.Canary, StringComparison.Ordinal)).IsFalse();
        var envelope = await ReadAsync(fixture);
        await Assert.That(envelope.Disposition).IsEqualTo("measured");
        await Assert.That(envelope.Reason).IsNull();
        var report = envelope.Report ?? throw new InvalidOperationException("The failed native report was discarded.");
        await Assert.That(report.SchemaVersion).IsEqualTo(3);
        await Assert.That(report.Options).IsEqualTo(IsolatedComparisonContract.Current.Options);
        await Assert.That(report.SourceRevision).IsEqualTo(ComparisonExecutionIdentitySupport.Revision);
        await Assert.That(report.Provenance).IsEqualTo(new GitHubProvenance(37070000000, 2, "managedcode/KeyLoad",
            "refs/heads/main", "CI", "intensive-1k-c16"));
        await Assert.That(report.LoadGeneratorImage).IsEqualTo(ComparisonExecutionIdentitySupport.LoadGeneratorImageValue);
        await Assert.That(report.Targets.Single().Name).IsEqualTo("Neo4j");
        await Assert.That(report.Targets.Single().Image).IsEqualTo(ComparisonExecutionIdentitySupport.KeyLoadImageValue);
        await Assert.That(report.Targets.Single().Cluster).IsNull();
        await Assert.That(report.Cases.Length).IsEqualTo(5);
        await Assert.That(report.Cases.All(item => item.Status == "failed" && item.Scenario == Scenario.PointRead
            && item.Measurement is null && item.Samples.IsEmpty && item.Detail is not null)).IsTrue();
        await Assert.That(report.Cases.Select(item => item.Repetition).SequenceEqual([0, 1, 2, 3, 4])).IsTrue();
        await Assert.That((await File.ReadAllTextAsync(Path.Combine(fixture.DirectoryPath, "worker.json")))
            .Contains(IsolatedHostFixture.Canary, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task<IsolatedComparisonReport> ReadAsync(IsolatedHostFixture fixture)
    {
        await using var stream = File.OpenRead(Path.Combine(fixture.DirectoryPath, "worker.json"));
        return await JsonSerializer.DeserializeAsync<IsolatedComparisonReport>(stream, ReportWriter.JsonOptions)
            ?? throw new InvalidOperationException("A complete worker envelope was not published.");
    }
}
