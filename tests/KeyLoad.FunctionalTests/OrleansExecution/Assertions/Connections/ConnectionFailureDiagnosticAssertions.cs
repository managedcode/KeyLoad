using KeyLoad.Clustering;

namespace KeyLoad.FunctionalTests.OrleansExecution;

/// <summary>Checks actual execution failure diagnostics together with original connection effects and replay.</summary>
internal static class ConnectionFailureDiagnosticAssertions
{
    private const string SafeUnavailable = "The database request could not complete. Retry the same command ID for writes.";
    private const string RejectedPrefix = "Orleans request rejected: ";
    private const string StorageCause = "Closed cause StorageIo.";
    private const int OneDiagnostic = 1;
    private const long FirstRevision = 1;

    internal static async Task FailedAsync(ConnectionScenario scenario, Guid requestId,
        GrainOperationReply reply, EventSourceLogCapture capture, CancellationToken token)
    {
        await scenario.Observation.Read(requestId).Disposed.Task.WaitAsync(token);
        await Assert.That(reply.Result).IsNull();
        await Assert.That(reply.SafeDetail).IsEqualTo(SafeUnavailable);
        var rows = capture.Messages.Where(row => row.Contains(RejectedPrefix + requestId,
            StringComparison.Ordinal)).ToArray();
        await Assert.That(rows.Length).IsEqualTo(OneDiagnostic);
        var row = rows.Single();
        await Assert.That(row).Contains(nameof(GrainFailureStage.EnvelopeVerification));
        await Assert.That(row).Contains(nameof(GrainFailureCategory.Unexpected));
        await Assert.That(row).Contains(nameof(ErrorCode.UnknownWriteOutcome));
        await Assert.That(row).Contains(StorageCause);
        await Assert.That(row).DoesNotContain(ConnectionProtocol.OrdinaryFailure);
        await Assert.That(row).DoesNotContain(nameof(IOException));
        await Assert.That(row).DoesNotContain(ConnectionProtocol.FirstJson);
        await Assert.That(reply.SafeDetail).DoesNotContain(ConnectionProtocol.OrdinaryFailure);
        await Assert.That(reply.SafeDetail).DoesNotContain(nameof(IOException));
    }

    internal static async Task ReplayAsync(ConnectionScenario scenario, PrincipalRecord principal,
        Guid originalRequestId, CommandRequest command, GrainOperationReply original, CancellationToken token)
    {
        var before = scenario.Fixture.Database.ReadCanonicalCut();
        var position = scenario.Fixture.Database.Store.Position;
        var replayId = Guid.NewGuid();
        var replay = await scenario.CommandAsync(principal, replayId, command, token);
        await Assert.That(replay.Error).IsNull();
        await Assert.That(replay.SafeDetail).IsEqualTo(original.SafeDetail);
        await Assert.That(OrleansSerialization.Serialize(GrainReplyFactory.RequireValue(replay)).AsSpan().SequenceEqual(OrleansSerialization.Serialize(GrainReplyFactory.RequireValue(original)))).IsTrue();
        await Assert.That(scenario.Fixture.Database.ReadCanonicalCut()).IsEqualTo(before);
        await Assert.That(scenario.Fixture.Database.Store.Position).IsEqualTo(position);
        await ConnectionAssertions.StoredAsync(scenario, "healthy-after-revocation",
            FirstRevision, ConnectionProtocol.FirstJson);
        await ConnectionAssertions.SameOwnerAsync(scenario, originalRequestId, replayId, token);
    }
}
