using KeyLoad.Clustering;
using KeyLoad.FunctionalTests.OrleansExecution;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class IssuedPurposeOperation
{
    private const string DocumentId = "genuine-purpose-repair";
    private const string Json = "{\"purpose\":\"current\",\"text\":\"Україна\"}";
    private const string InvalidScopeDetail = "The internal request has an invalid scope, key, expiry or operation.";

    internal static async Task RunAsync(ConnectionScenario scenario,
        IssuedPurposeObservation observation, CancellationToken token)
    {
        var principal = scenario.Principal();
        var command = scenario.Command(DocumentId, Json);
        var before = IssuedPurposeCut.Read(scenario.Fixture.Database);
        observation.Arm(command.CommandId);
        GrainOperationReply? partial = null;
        var refused = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial =
            await scenario.CommandAsync(principal, Guid.NewGuid(), command, token))
            ?? throw new InvalidOperationException("The original purpose did not refuse before routing.");
        await Assert.That(refused.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(refused.Message).IsEqualTo(InvalidScopeDetail);
        await Assert.That(partial).IsNull();
        await IssuedPurposeCut.RequireAsync(before, scenario.Fixture.Database);
        await Assert.That(scenario.Stored(DocumentId)).IsNull();
        observation.Disarm();
        var healthy = await scenario.CommandAsync(principal, Guid.NewGuid(), command, token);
        await Assert.That(healthy.Error).IsNull();
        var receipt = ConnectionAssertions.Value<CommitReceipt>(healthy);
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await ConnectionAssertions.StoredAsync(scenario, DocumentId, 1, Json);
        var committed = IssuedPurposeCut.Read(scenario.Fixture.Database);
        var replay = await scenario.CommandAsync(principal, Guid.NewGuid(), command, token);
        await Assert.That(replay.Error).IsNull();
        await Assert.That(OrleansSerialization.Serialize(GrainReplyFactory.RequireValue(healthy)).AsSpan().SequenceEqual(OrleansSerialization.Serialize(GrainReplyFactory.RequireValue(replay)))).IsTrue();
        await IssuedPurposeCut.RequireAsync(committed, scenario.Fixture.Database);
        var coldSource = new ConnectionColdSource(scenario.Fixture.Source);
        var reference = scenario.Reference(DocumentId);
        await scenario.CloseAsync(token);
        await scenario.Fixture.Hosts.StopAsync(token);
        await IssuedPurposeCold.RequireAsync(coldSource, committed, reference, Json, token);
    }
}
