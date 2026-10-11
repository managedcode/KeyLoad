using System.Text.Json;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class NativeClusterStartupEvidenceTests
{
    private const string Document = "native-startup-following";

    [Test]
    public async Task OriginalCancelledStartupJoinsEvidenceThenFreshSignedWriteReplayAndCloseAreHealthy()
    {
        await using var failed = new RequestCqrsClusterFixture();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Exception? original = null;
        try
        { await failed.InitializeAsync(cancelled.Token); }
        catch (OperationCanceledException observed) { original = observed; }
        await Assert.That(original).IsNotNull();
        using var evidence = JsonDocument.Parse(await File.ReadAllBytesAsync(
            NativeClusterStartupEvidence.PathFor(failed.Cluster.Options.ClusterId)));
        await Assert.That(evidence.RootElement.GetProperty(NativeClusterStartupProtocol.CleanupJoined).GetBoolean()).IsTrue();
        await Assert.That(evidence.RootElement.GetProperty(NativeClusterStartupProtocol.CallerCancelled).GetBoolean()).IsTrue();
        await Assert.That(evidence.RootElement.GetProperty(NativeClusterStartupProtocol.DeadlineCancelled).GetBoolean()).IsFalse();
        await Assert.That(evidence.RootElement.GetProperty(NativeClusterStartupProtocol.Files).GetArrayLength()).IsEqualTo(0);
        await Assert.That(evidence.RootElement.GetProperty(NativeClusterStartupProtocol.ExceptionTypes)[0].GetString())
            .IsEqualTo(typeof(OperationCanceledException).FullName);
        await HealthyAsync();
    }

    private static async Task HealthyAsync()
    {
        string? clusterId = null;
        var logRoot = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        await ConnectionNativeScenario.RunAsync(async scenario =>
        {
            clusterId = scenario.Fixture.Cluster.Options.ClusterId;
            using var timeout = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
            var token = timeout.Token;
            await scenario.InitializeAsync(token);
            var principal = scenario.Principal();
            var command = scenario.Command(Document);
            var original = await scenario.CommandAsync(principal, Guid.NewGuid(), command, token);
            var replay = await scenario.CommandAsync(principal, Guid.NewGuid(), command, token);
            await Assert.That(original.Error).IsNull();
            await Assert.That(replay.Error).IsNull();
            await Assert.That(ConnectionNativeAssertions.Value<CommitReceipt>(original).CommandId)
                .IsEqualTo(command.CommandId);
            await Assert.That(original.Payload.Span.SequenceEqual(replay.Payload.Span)).IsTrue();
            var read = await scenario.ReadAsync(principal, Guid.NewGuid(), Document, token);
            await Assert.That(read.Error).IsNull();
            var document = ConnectionNativeAssertions.Value<DocumentResult>(read);
            await Assert.That(document.Json).IsEqualTo(ConnectionNativeProtocol.FirstJson);
            await Assert.That(document.Reference).IsEqualTo(scenario.Reference(Document));
            await ConnectionNativeAssertions.StoredAsync(scenario, Document, 1, ConnectionNativeProtocol.FirstJson);
            await scenario.CloseAsync(token);
            await Assert.That(File.Exists(NativeClusterStartupEvidence.PathFor(
                scenario.Fixture.Cluster.Options.ClusterId))).IsFalse();
        });
        var owner = clusterId ?? throw new InvalidOperationException("The original native owner was not created.");
        var primary = Directory.EnumerateFiles(logRoot, owner + "_*.log")
            .Single(static path => path.EndsWith("_Primary.log", StringComparison.Ordinal));
        var trace = new NativeClusterStartupTrace();
        var inspected = trace.Read(primary, "Primary");
        await Assert.That(inspected.InspectedBytes).IsGreaterThan(0);
        await Assert.That(trace.Events.Any(static item => item.Event == "LogSiloStartingWithGC"
            && item.Category == "Orleans.Runtime.Silo")).IsTrue();
        await Assert.That(JsonSerializer.Serialize(trace.Events).Contains(
            ConnectionNativeProtocol.FirstJson, StringComparison.Ordinal)).IsFalse();
    }
}
