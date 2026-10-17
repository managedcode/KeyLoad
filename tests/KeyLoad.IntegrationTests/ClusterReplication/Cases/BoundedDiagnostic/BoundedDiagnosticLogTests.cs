using System.Text;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal sealed class BoundedDiagnosticLogTests
{
    private const int MaximumLines = 80;
    private const int MaximumBytes = 8_192;
    private const int NoiseCharacters = 20_000;

    [Test]
    public async Task AcDiag003EveryNodeRetainsItsFailureBeforeOversizedNoise()
    {
        var groups = Enumerable.Range(1, 3).Select(node => new[]
        {
            $"node{node}: state=Running",
            $"node{node}: Database request failed phase=CommandDispatch category=Timeout",
            new string('x', NoiseCharacters)
        });
        var lines = BoundedDiagnosticLog.BoundNodes(groups);
        await Assert.That(lines.Length <= MaximumLines).IsTrue();
        await Assert.That(Encoding.UTF8.GetByteCount(string.Join(Environment.NewLine, lines) + Environment.NewLine)
            <= MaximumBytes).IsTrue();
        foreach (var node in Enumerable.Range(1, 3))
        {
            await Assert.That(lines).Contains(line => line.StartsWith($"node{node}: Database request failed", StringComparison.Ordinal));
        }
    }

    [Test]
    public async Task AcDiag003UnicodeClippingPreservesScalarsAndNodeShares()
    {
        var text = string.Concat(Enumerable.Repeat("😀", NoiseCharacters));
        var groups = Enumerable.Range(1, 3).Select(node => new[] { $"node{node}: state=Running", text });
        var lines = BoundedDiagnosticLog.BoundNodes(groups);
        var output = string.Join(Environment.NewLine, lines) + Environment.NewLine;
        await Assert.That(Encoding.UTF8.GetByteCount(output) <= MaximumBytes).IsTrue();
        await Assert.That(output.Contains('\uFFFD', StringComparison.Ordinal)).IsFalse();
        await Assert.That(lines.Count(line => line.StartsWith("node", StringComparison.Ordinal))).IsEqualTo(3);
        await Assert.That(Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(output))).IsEqualTo(output);
    }

    [Test]
    public async Task AcDiag003ExistingSingleArtifactBoundsRemainExact()
    {
        var lines = BoundedDiagnosticLog.Bound(Enumerable.Repeat("short", MaximumLines + 1));
        await Assert.That(lines.Length).IsEqualTo(MaximumLines);
        await Assert.That(BoundedDiagnosticLog.BoundNodes([]).Length).IsEqualTo(0);
    }
}
