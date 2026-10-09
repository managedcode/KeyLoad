using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ConnectionRf3SequentialTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task AcCrs057SdkAndOfficialMcpReusePhysicalConnectionAfterFailureAndReplay(bool useMcp)
        => ConnectionRf3Scenario.RunAsync((scenario, token) => ExecuteAsync(scenario, useMcp, token),
            TestContext.Current!.Execution.CancellationToken);

    private static async Task ExecuteAsync(ConnectionRf3Scenario scenario, bool useMcp, CancellationToken token)
    {
        var caller = await scenario.CallerAsync(token);
        var identity = scenario.Identity;
        var command = RequestCqrsPhaseFaultProvisioning.UpdateCommand(identity, Guid.NewGuid());
        var first = await scenario.ObserveAsync(identity, command.CommandId, null,
            () => ConnectionRf3PublicOperations.CommitAsync(caller, command, useMcp, token), token);
        await ConnectionRf3PublicOperations.ReceiptAsync(first.Result, command, first.Witness);
        var replay = await scenario.ObserveAsync(identity, command.CommandId, null,
            () => ConnectionRf3PublicOperations.CommitAsync(caller, command, useMcp, token), token);
        await ConnectionRf3PublicOperations.ReceiptAsync(replay.Result, command, replay.Witness);
        await Assert.That(replay.Result.Value.Token).IsEqualTo(first.Result.Value.Token);
        await Assert.That(replay.Result.Value.Durability).IsEqualTo(first.Result.Value.Durability);
        await Assert.That(replay.Result.Value.Mutations.SequenceEqual(first.Result.Value.Mutations)).IsTrue();
        await ConnectionRf3WitnessReader.SameOwnerAsync(first.Witness, replay.Witness);
        var invalid = new CommandRequest(Guid.NewGuid(), identity.Partition,
            [new PutDocument(RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId,
                RequestCqrsRf3Protocol.DocumentJson, ExpectedRevision: ConnectionRf3Protocol.InvalidExpectedRevision,
                ExplicitReplacement: true)]);
        var failed = await scenario.ObserveAsync(identity, invalid.CommandId, null, async () =>
        {
            await ConnectionRf3PublicOperations.ErrorAsync(caller, invalid, useMcp, ErrorCode.RevisionConflict, token);
            return true;
        }, token);
        await Assert.That(failed.Result).IsTrue();
        await ConnectionRf3WitnessReader.SameOwnerAsync(first.Witness, failed.Witness);
        var read = await scenario.ObserveAsync(identity, Guid.Empty, GrainReadKind.Document,
            () => ConnectionRf3PublicOperations.ReadAsync(caller, identity, useMcp, token), token);
        await ConnectionRf3PublicOperations.DocumentAsync(read.Result, identity, read.Witness,
            RequestCqrsRf3Protocol.ChangedDocumentJson, ConnectionRf3Protocol.UpdatedRevision);
        await ConnectionRf3WitnessReader.SameOwnerAsync(first.Witness, read.Witness);
        await scenario.VerifyDocumentAsync(identity, RequestCqrsRf3Protocol.ChangedDocumentJson,
            ConnectionRf3Protocol.UpdatedRevision, token);
        var closed = await scenario.CloseAsync(caller, first.Marker, token);
        await Assert.That(closed.ConnectionId).IsEqualTo(first.Witness.ConnectionId);
        await Assert.That(closed.ActivationId).IsEqualTo(first.Witness.ActivationId);
        await Assert.That(closed.SelectedActivationCount).IsEqualTo(ConnectionRf3Protocol.Absent);
    }
}
