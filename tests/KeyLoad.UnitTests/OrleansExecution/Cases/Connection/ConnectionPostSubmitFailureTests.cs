using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.OrleansExecution;

internal sealed class ConnectionPostSubmitFailureTests
{
    private const long OriginalRevision = 1;
    private const long WrongRevision = 0;
    private const string DocumentId = "post-submit-original";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ReturnedSubmitObserverFailureIsUnknownAndOriginalReplayRemainsExact(bool tagged)
    {
        ConnectionPostSubmitFailureObserver? observer = null;
        return ConnectionScenario.RunAsync(async scenario =>
        {
            using var timeout = new CancellationTokenSource(scenario.Timing.CompletionTimeout, TimeProvider.System);
            var token = timeout.Token;
            await scenario.InitializeAsync(token);
            var principal = scenario.Principal();
            var requestId = Guid.NewGuid();
            ArgumentNullException.ThrowIfNull(observer);
            observer.RequestId = requestId;
            observer.Tagged = tagged;
            var held = scenario.Observation.Hold(requestId, GrainRequestPhase.SubmitReturned);
            var command = scenario.Command(DocumentId);
            var original = scenario.CommandAsync(principal, requestId, command, token);
            await held.Arrived.Task.WaitAsync(token);
            var outcome = OutcomeStoreOracle.ReadPartition(scenario.Fixture.Database.Store,
                command.Partition, principal.Id, command.CommandId)
                ?? throw new InvalidOperationException(ConnectionProtocol.MissingObservation);
            await Assert.That(Errors.Code(outcome.Problem)).IsNull();
            var originalBytes = OrleansSerialization.Serialize(outcome);
            await ConnectionAssertions.StoredAsync(scenario, DocumentId, OriginalRevision, ConnectionProtocol.FirstJson);
            held.Release.TrySetResult();
            var failure = await original;
            await Assert.That(observer.Seen).IsTrue();
            await Assert.That(failure.Error).IsEqualTo(ErrorCode.UnknownWriteOutcome);
            await Assert.That(failure.SafeDetail).DoesNotContain(ConnectionPostSubmitFailureObserver.PrivateFailure);
            var replayRequestId = Guid.NewGuid();
            await Assert.That((await scenario.CommandAsync(principal, replayRequestId, command, token)).Error).IsNull();
            var replayed = OutcomeStoreOracle.ReadPartition(scenario.Fixture.Database.Store,
                command.Partition, principal.Id, command.CommandId)
                ?? throw new InvalidOperationException(ConnectionProtocol.MissingObservation);
            await Assert.That(OrleansSerialization.Serialize(replayed).SequenceEqual(originalBytes)).IsTrue();
            await ConnectionAssertions.StoredAsync(scenario, DocumentId, OriginalRevision, ConnectionProtocol.FirstJson);
            await ConnectionAssertions.SameOwnerAsync(scenario, requestId, replayRequestId, token);
            await RequirePersistedRefusalAsync(scenario, principal, token);
            await scenario.CloseAsync(token);
        }, phaseObserver: original => observer = new(original));
    }

    private static async Task RequirePersistedRefusalAsync(ConnectionScenario scenario,
        PrincipalRecord principal, CancellationToken token)
    {
        var refused = scenario.Command(DocumentId, expectedRevision: WrongRevision);
        var reply = await scenario.CommandAsync(principal, Guid.NewGuid(), refused, token);
        await Assert.That(reply.Error).IsEqualTo(ErrorCode.RevisionConflict);
        var stored = OutcomeStoreOracle.ReadPartition(scenario.Fixture.Database.Store,
            refused.Partition, principal.Id, refused.CommandId)
            ?? throw new InvalidOperationException(ConnectionProtocol.MissingObservation);
        await Assert.That(Errors.Code(stored.Problem)).IsNotNull();
        await Assert.That(reply.Error).IsEqualTo(Errors.Code(stored.Problem));
        await Assert.That(reply.SafeDetail).IsEqualTo(stored.Problem?.Detail);
        await ConnectionAssertions.StoredAsync(scenario, DocumentId, OriginalRevision, ConnectionProtocol.FirstJson);
    }
}
