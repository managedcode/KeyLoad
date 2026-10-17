using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Checks actual native failure diagnostics together with original connection effects and replay.</summary>
internal static class ConnectionFailureDiagnosticAssertions
{
    private const string SafeUnavailable = "The database request could not complete. Retry the same command ID for writes.";
    private const string RejectedPrefix = "Orleans request rejected: ";
    private const string StorageCause = "Closed cause StorageIo.";
    private const int OneDiagnostic = 1;
    private const long FirstRevision = 1;

    internal static async Task FailedAsync(ConnectionNativeScenario scenario, Guid requestId,
        GrainOperationReply reply, EventSourceLogCapture capture, CancellationToken token)
    {
        await scenario.Observation.Read(requestId).Disposed.Task.WaitAsync(token);
        await Assert.That(reply.Payload.IsEmpty).IsTrue();
        await Assert.That(reply.SafeDetail).IsEqualTo(SafeUnavailable);
        var rows = capture.Messages.Where(row => row.Contains(RejectedPrefix + requestId,
            StringComparison.Ordinal)).ToArray();
        await Assert.That(rows.Length).IsEqualTo(OneDiagnostic);
        var row = rows.Single();
        await Assert.That(row).Contains(nameof(GrainFailureStage.CapabilityExecution));
        await Assert.That(row).Contains(nameof(GrainFailureCategory.Unexpected));
        await Assert.That(row).Contains(nameof(ErrorCode.UnknownWriteOutcome));
        await Assert.That(row).Contains(StorageCause);
        await Assert.That(row).DoesNotContain(ConnectionNativeProtocol.OrdinaryFailure);
        await Assert.That(row).DoesNotContain(nameof(IOException));
        await Assert.That(row).DoesNotContain(ConnectionNativeProtocol.FirstJson);
        await Assert.That(reply.SafeDetail).DoesNotContain(ConnectionNativeProtocol.OrdinaryFailure);
        await Assert.That(reply.SafeDetail).DoesNotContain(nameof(IOException));
    }

    internal static async Task ReplayAsync(ConnectionNativeScenario scenario, PrincipalRecord principal,
        Guid originalRequestId, CommandRequest command, GrainOperationReply original, CancellationToken token)
    {
        var before = scenario.Fixture.Database.ReadJournalCut();
        var position = scenario.Fixture.Database.Store.Position;
        var replayId = Guid.NewGuid();
        var replay = await scenario.CommandAsync(principal, replayId, command, token);
        await Assert.That(replay.Error).IsNull();
        await Assert.That(replay.SafeDetail).IsEqualTo(original.SafeDetail);
        await Assert.That(replay.Payload.Span.SequenceEqual(original.Payload.Span)).IsTrue();
        await Assert.That(scenario.Fixture.Database.ReadJournalCut()).IsEqualTo(before);
        await Assert.That(scenario.Fixture.Database.Store.Position).IsEqualTo(position);
        await ConnectionNativeAssertions.StoredAsync(scenario, "healthy-after-revocation",
            FirstRevision, ConnectionNativeProtocol.FirstJson);
        await ConnectionNativeAssertions.SameOwnerAsync(scenario, originalRequestId, replayId, token);
    }
}
