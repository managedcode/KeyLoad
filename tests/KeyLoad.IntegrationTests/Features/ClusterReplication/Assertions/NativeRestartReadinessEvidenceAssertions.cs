namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class NativeRestartReadinessEvidenceAssertions
{
    private const string HealthyLine = ": Readiness HTTP 200. branch Unobserved.";
    private const string ObservationLine = ": native log subscription ";

    internal static async Task HealthyAsync(ClusterFixture fixture, CancellationToken cancellationToken)
    {
        await fixture.SaveFailureDiagnosticsAsync(cancellationToken);
        var path = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            ClusterFixtureProtocol.ArtifactDirectory, ClusterFixtureProtocol.QualificationDirectory,
            ClusterFixtureProtocol.DiagnosticsFileName);
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
