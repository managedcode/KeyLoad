using KeyLoad.Clustering;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class ConnectionClosedCommandContinuation
{
    private const long InitialRevision = 1;
    private const long InitialOwnershipEpoch = 1;
    private const long NextCommit = 1;
    private const string OriginalDocument = "closed-original";

    internal static async Task RunAsync(ConnectionNativeScenario scenario, PrincipalRecord principal,
        CommandRequest command, Guid previousRequest, CancellationToken token)
    {
        var ownerId = Guid.NewGuid();
        var owner = scenario.Fixture.Cluster.Client.GetGrain<IConnectionGrain>(ownerId);
        var failures = new List<Exception>();
        try
        {
            var originalIdentity = scenario.Fixture.Database.Store.Identity;
            var expectedPosition = scenario.Fixture.Database.Store.Position + NextCommit;
            var requestId = Guid.NewGuid();
            var created = await ConnectionAwaitedSettlementCalls.CommandForOwnerAsync(scenario, principal,
                requestId, command, ownerId, token);
            await Assert.That(created.Error).IsNull();
            var receipt = ConnectionNativeAssertions.Value<CommitReceipt>(created);
            await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
            var expectedReceipt = new CommitReceipt(command.CommandId,
                new(originalIdentity.Incarnation, command.Partition.AtomicPartitionId, expectedPosition, InitialOwnershipEpoch),
                [new("putDocument", ConnectionNativeProtocol.Collection, OriginalDocument, InitialRevision)], originalIdentity.Durability);
            await Assert.That(JsonDefaults.Serialize(receipt).SequenceEqual(JsonDefaults.Serialize(expectedReceipt))).IsTrue();
            await Assert.That(JsonDefaults.Serialize(receipt.Mutations).SequenceEqual(JsonDefaults.Serialize(
                new MutationReceipt[] { new("putDocument", ConnectionNativeProtocol.Collection,
                    OriginalDocument, InitialRevision) }))).IsTrue();
            await Assert.That(JsonDefaults.Serialize(scenario.Stored(OriginalDocument)).SequenceEqual(
                JsonDefaults.Serialize(new DocumentResult(scenario.Reference(OriginalDocument), InitialRevision,
                    ConnectionNativeProtocol.FirstJson, false, [])))).IsTrue();
            var observed = scenario.Observation.Read(requestId);
            await observed.Disposed.Task.WaitAsync(token);
            await Assert.That(observed.Identity.PrincipalId).IsEqualTo(principal.Id);
            await Assert.That(observed.GrainId).IsNotEqualTo(scenario.Observation.Read(previousRequest).GrainId);
            var image = QueueWholeFlowStorage.Bytes(scenario.Fixture.Database.Store);
            var cut = scenario.Fixture.Database.Store.Position;
            var replayId = Guid.NewGuid();
            var replay = await ConnectionAwaitedSettlementCalls.CommandForOwnerAsync(scenario, principal,
                replayId, command, ownerId, token);
            await Assert.That(replay.Error).IsNull();
            await Assert.That(replay.Payload.Span.SequenceEqual(created.Payload.Span)).IsTrue();
            await scenario.Observation.Read(replayId).Disposed.Task.WaitAsync(token);
            await Assert.That(QueueWholeFlowStorage.Bytes(scenario.Fixture.Database.Store).SequenceEqual(image)).IsTrue();
            await Assert.That(scenario.Fixture.Database.Store.Position).IsEqualTo(cut);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        finally
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                () => owner.CloseAsync(scenario.Fixture.Codec.CreateConnectionClose(ownerId), token), failures);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        await scenario.AssertActivationCountAsync(ConnectionNativeProtocol.AbsentActivations, token);
    }
}
