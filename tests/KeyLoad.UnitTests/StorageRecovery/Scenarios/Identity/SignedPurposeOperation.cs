using KeyLoad.UnitTests.Features.OrleansExecution;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class SignedPurposeOperation
{
    private const string DocumentId = "genuine-purpose-repair";
    private const string Json = "{\"purpose\":\"current\",\"text\":\"Україна\"}";

    internal static async Task RunAsync(ConnectionScenario scenario,
        SignedPurposeObservation observation, CancellationToken token)
    {
        await scenario.InitializeAsync(token);
        var principal = scenario.Principal();
        var command = scenario.Command(DocumentId, Json);
        var before = SignedPurposeCut.Read(scenario.Fixture.Database);
        observation.Arm(command.CommandId);
        var refused = await scenario.CommandAsync(principal, Guid.NewGuid(), command, token);
        await Assert.That(refused.Error).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(refused.Payload.IsEmpty).IsTrue();
        await SignedPurposeCut.RequireAsync(before, scenario.Fixture.Database);
        await Assert.That(scenario.Stored(DocumentId)).IsNull();
        observation.Disarm();
        var healthy = await scenario.CommandAsync(principal, Guid.NewGuid(), command, token);
        await Assert.That(healthy.Error).IsNull();
        var receipt = ConnectionAssertions.Value<CommitReceipt>(healthy);
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await ConnectionAssertions.StoredAsync(scenario, DocumentId, 1, Json);
        var committed = SignedPurposeCut.Read(scenario.Fixture.Database);
        var replay = await scenario.CommandAsync(principal, Guid.NewGuid(), command, token);
        await Assert.That(replay.Error).IsNull();
        await Assert.That(healthy.Payload.Span.SequenceEqual(replay.Payload.Span)).IsTrue();
        await SignedPurposeCut.RequireAsync(committed, scenario.Fixture.Database);
        var coldSource = new ConnectionColdSource(scenario.Fixture.Source);
        var reference = scenario.Reference(DocumentId);
        await scenario.CloseAsync(token);
        await scenario.Fixture.Hosts.StopAsync(token);
        await SignedPurposeCold.RequireAsync(coldSource, committed, reference, Json, token);
    }
}
