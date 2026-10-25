namespace KeyLoad.ClusterTests.StorageReplication;

internal static class RestartReadinessEvidenceAssertions
{
    private const string HealthyLine = ": Readiness HTTP 200. branch Unobserved.";
    private const string ObservationLine = ": execution log subscription ";

    internal static async Task HealthyAsync(ClusterFixture fixture, CancellationToken cancellationToken)
    {
        var path = await fixture.SaveFailureDiagnosticsAsync(cancellationToken);
        var lines = await File.ReadAllLinesAsync(path, cancellationToken);
        foreach (var number in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber,
            ClusterFixtureProtocol.NodeCount))
        {
            var node = ClusterFixtureProtocol.NodeName(number);
            await Assert.That(lines.Contains(node + HealthyLine, StringComparer.Ordinal)).IsTrue();
            await Assert.That(lines.Any(line => line.StartsWith(node + ObservationLine,
                StringComparison.Ordinal))).IsTrue();
        }
    }
}
