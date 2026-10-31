using System.Text.Json;

using KeyLoad.Clustering;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal sealed class ClusterStartupEvidenceTests
{
    private const string Document = "execution-startup-following";
    private const string KindKey = "kind";
    private const string MissingFilesKey = "missingFiles";
    private const string EventsKey = "events";
    private const int ExpectedServerFiles = 2;

    [Test]
    public async Task OriginalCancelledStartupJoinsEvidenceThenFreshWriteReplayAndCloseAreHealthy()
    {
        await using var failed = new RequestCqrsHostedRuntimeFixture();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Exception? original = null;
        try
        {
            await failed.InitializeAsync(cancelled.Token);
        }
        catch (OperationCanceledException observed) { original = observed; }
        await Assert.That(original).IsNotNull();
        await failed.DisposeAsync();
        await Assert.That(failed.IsDisposalJoined).IsTrue();
        await Assert.That(failed.OwnedDirectoryExists).IsFalse();
        await OperationFailureAsync();
        await HealthyAsync();
    }

    private static async Task OperationFailureAsync()
    {
        string? clusterId = null;
        OperationCanceledException? initiating = null;
        Exception? observed = null;
        try
        {
            await ConnectionScenario.RunAsync(async scenario =>
            {
                using var timeout = new CancellationTokenSource(scenario.Timing.StartupTimeout, TimeProvider.System);
                await scenario.InitializeAsync(timeout.Token);
                timeout.CancelAfter(scenario.Timing.CompletionTimeout);
                clusterId = scenario.Fixture.Evidence.ClusterId;
                var principal = scenario.Principal();
                using var cancelled = new CancellationTokenSource();
                await cancelled.CancelAsync();
                try
                {
                    await scenario.CommandAsync(principal, Guid.NewGuid(), scenario.Command(Document), cancelled.Token);
                }
                catch (OperationCanceledException failure)
                {
                    initiating = failure;
                }
                await Assert.That(initiating).IsNotNull();
                await Assert.That(scenario.Stored(Document)).IsNull();
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(
                    initiating ?? throw new InvalidOperationException("The operation did not cancel.")).Throw();
            });
        }
        catch (OperationCanceledException failure) { observed = failure; }
        await Assert.That(ReferenceEquals(observed, initiating)).IsTrue();
        using var evidence = JsonDocument.Parse(await File.ReadAllBytesAsync(
            ClusterStartupEvidence.OperationPathFor(clusterId
                ?? throw new InvalidOperationException("The original operation owner was not created."))));
        var record = evidence.RootElement;
        await Assert.That(record.GetProperty(KindKey).GetString()).IsEqualTo(ClusterStartupProtocol.OperationKind);
        await Assert.That(record.GetProperty(ClusterStartupProtocol.CleanupJoined).GetBoolean()).IsTrue();
        await Assert.That(record.GetProperty(ClusterStartupProtocol.Files).GetArrayLength()).IsEqualTo(ExpectedServerFiles);
        await Assert.That(record.GetProperty(MissingFilesKey).GetInt32()).IsEqualTo(0);
        await Assert.That(record.GetProperty(EventsKey).GetArrayLength()).IsGreaterThan(0);
        await Assert.That(record.GetRawText().Contains(ConnectionProtocol.FirstJson, StringComparison.Ordinal)).IsFalse();
    }

    private static async Task HealthyAsync()
    {
        string? clusterId = null;
        ConnectionServerRuntime? runtime = null;
        await ConnectionScenario.RunAsync(async scenario =>
        {
            using var timeout = new CancellationTokenSource(scenario.Timing.StartupTimeout, TimeProvider.System);
            var token = timeout.Token;
            await scenario.InitializeAsync(token);
            timeout.CancelAfter(scenario.Timing.CompletionTimeout);
            clusterId = scenario.Fixture.Evidence.ClusterId;
            runtime = scenario.Fixture;
            var principal = scenario.Principal();
            var command = scenario.Command(Document);
            var original = await scenario.CommandAsync(principal, Guid.NewGuid(), command, token);
            var replay = await scenario.CommandAsync(principal, Guid.NewGuid(), command, token);
            await Assert.That(original.Error).IsNull();
            await Assert.That(replay.Error).IsNull();
            await Assert.That(ConnectionAssertions.Value<CommitReceipt>(original).CommandId)
                .IsEqualTo(command.CommandId);
            await Assert.That(OrleansSerialization.Serialize(GrainReplyFactory.RequireValue(original)).AsSpan().SequenceEqual(OrleansSerialization.Serialize(GrainReplyFactory.RequireValue(replay)))).IsTrue();
            var read = await scenario.ReadAsync(principal, Guid.NewGuid(), Document, token);
            await Assert.That(read.Error).IsNull();
            var document = ConnectionAssertions.Value<DocumentResult>(read);
            await Assert.That(document.Json).IsEqualTo(ConnectionProtocol.FirstJson);
            await Assert.That(document.Reference).IsEqualTo(scenario.Reference(Document));
            await ConnectionAssertions.StoredAsync(scenario, Document, 1, ConnectionProtocol.FirstJson);
            await scenario.CloseAsync(token);
            await Assert.That(File.Exists(ClusterStartupEvidence.OperationPathFor(
                scenario.Fixture.Evidence.ClusterId))).IsFalse();
            await Assert.That(File.Exists(ClusterStartupEvidence.PathFor(
                scenario.Fixture.Evidence.ClusterId))).IsFalse();
        });
        var owner = runtime ?? throw new InvalidOperationException("The original execution owner was not created.");
        await Assert.That(clusterId).IsEqualTo(owner.Evidence.ClusterId);
        await Assert.That(owner.IsDisposalJoined).IsTrue();
        var trace = new ClusterStartupTrace();
        var files = owner.Evidence.ReadJoined(owner.IsDisposalJoined, trace);
        await Assert.That(files.Count).IsEqualTo(ExpectedServerFiles);
        foreach (var file in files)
        {
            await Assert.That(file.InspectedBytes).IsGreaterThan(0);
        }
        await Assert.That(trace.Events.Any(item => item.Role == files[0].Role && item.Event == "LogSiloStartingWithGC"
            && item.Category == "Orleans.Runtime.Silo")).IsTrue();
        await Assert.That(JsonSerializer.Serialize(trace.Events).Contains(
            ConnectionProtocol.FirstJson, StringComparison.Ordinal)).IsFalse();
    }
}
