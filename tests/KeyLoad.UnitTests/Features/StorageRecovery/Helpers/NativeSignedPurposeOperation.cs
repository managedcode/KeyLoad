using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class NativeSignedPurposeOperation
{
    private const string DocumentId = "genuine-purpose-repair";
    private const string Json = "{\"purpose\":\"current\",\"text\":\"Україна\"}";

    internal static async Task RunAsync(ConnectionNativeScenario scenario,
        NativeSignedPurposeObservation observation, CancellationToken token)
    {
        await scenario.InitializeAsync(token);
        var principal = scenario.Principal();
        var command = scenario.Command(DocumentId, Json);
        var before = NativeSignedPurposeCut.Read(scenario.Fixture.Database);
        observation.Arm(command.CommandId);
        var refused = await scenario.CommandAsync(principal, Guid.NewGuid(), command, token);
        await Assert.That(refused.Error).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(refused.Payload.IsEmpty).IsTrue();
        await NativeSignedPurposeCut.RequireAsync(before, scenario.Fixture.Database);
        await Assert.That(scenario.Stored(DocumentId)).IsNull();
        observation.Disarm();
        var healthy = await scenario.CommandAsync(principal, Guid.NewGuid(), command, token);
        await Assert.That(healthy.Error).IsNull();
        var receipt = ConnectionNativeAssertions.Value<CommitReceipt>(healthy);
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await ConnectionNativeAssertions.StoredAsync(scenario, DocumentId, 1, Json);
        var committed = NativeSignedPurposeCut.Read(scenario.Fixture.Database);
        var replay = await scenario.CommandAsync(principal, Guid.NewGuid(), command, token);
        await Assert.That(replay.Error).IsNull();
        await Assert.That(healthy.Payload.Span.SequenceEqual(replay.Payload.Span)).IsTrue();
        await NativeSignedPurposeCut.RequireAsync(committed, scenario.Fixture.Database);
        var coldSource = new ConnectionNativeColdSource(scenario.Fixture.Source);
        var reference = scenario.Reference(DocumentId);
        await scenario.CloseAsync(token);
        await scenario.Fixture.Hosts.StopAsync(token);
        await NativeSignedPurposeCold.RequireAsync(coldSource, committed, reference, Json, token);
    }
}
