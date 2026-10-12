using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

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
        return ConnectionNativeScenario.RunAsync(async scenario =>
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
                command.Partition, principal.Id, command.CommandId);
            await Assert.That(outcome).IsNotNull();
            await Assert.That(outcome!.Error).IsNull();
            var originalBytes = NativeSerialization.Serialize(outcome);
            await ConnectionNativeAssertions.StoredAsync(scenario, DocumentId, OriginalRevision, ConnectionNativeProtocol.FirstJson);
            held.Release.TrySetResult();
            var failure = await original;
            await Assert.That(observer.Seen).IsTrue();
            await Assert.That(failure.Error).IsEqualTo(ErrorCode.UnknownWriteOutcome);
            await Assert.That(failure.SafeDetail).DoesNotContain(ConnectionPostSubmitFailureObserver.PrivateFailure);
            var replayRequestId = Guid.NewGuid();
            await Assert.That((await scenario.CommandAsync(principal, replayRequestId, command, token)).Error).IsNull();
            var replayed = OutcomeStoreOracle.ReadPartition(scenario.Fixture.Database.Store,
                command.Partition, principal.Id, command.CommandId);
            await Assert.That(replayed).IsNotNull();
            await Assert.That(NativeSerialization.Serialize(replayed!).SequenceEqual(originalBytes)).IsTrue();
            await ConnectionNativeAssertions.StoredAsync(scenario, DocumentId, OriginalRevision, ConnectionNativeProtocol.FirstJson);
            await ConnectionNativeAssertions.SameOwnerAsync(scenario, requestId, replayRequestId, token);
            await RequirePersistedRefusalAsync(scenario, principal, token);
            await scenario.CloseAsync(token);
        }, phaseObserver: original => observer = new(original));
    }

    private static async Task RequirePersistedRefusalAsync(ConnectionNativeScenario scenario,
        PrincipalRecord principal, CancellationToken token)
    {
        var refused = scenario.Command(DocumentId, expectedRevision: WrongRevision);
        var reply = await scenario.CommandAsync(principal, Guid.NewGuid(), refused, token);
        await Assert.That(reply.Error).IsEqualTo(ErrorCode.RevisionConflict);
        var stored = OutcomeStoreOracle.ReadPartition(scenario.Fixture.Database.Store,
            refused.Partition, principal.Id, refused.CommandId);
        await Assert.That(stored).IsNotNull();
        await Assert.That(stored!.Error).IsNotNull();
        await Assert.That(reply.Error).IsEqualTo(stored.Error);
        await Assert.That(reply.SafeDetail).IsEqualTo(stored.SafeDetail);
        await ConnectionNativeAssertions.StoredAsync(scenario, DocumentId, OriginalRevision, ConnectionNativeProtocol.FirstJson);
    }
}
