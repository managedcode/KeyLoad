using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ConnectionRf3FreshIdleWindowFlow
{
    internal static async Task RunAsync(ConnectionRf3Scenario scenario, bool useMcp, CancellationToken token)
    {
        var caller = await scenario.CallerAsync(token).ConfigureAwait(false);
        var identity = scenario.Identity;
        var command = RequestCqrsPhaseFaultProvisioning.UpdateCommand(identity, Guid.NewGuid());
        var held = await scenario.StartHeldAsync(identity, command.CommandId, null,
            () => ConnectionRf3PublicOperations.CommitAsync(caller, command, useMcp, token), token).ConfigureAwait(false);
        await Assert.That(held.Original.IsCompleted).IsFalse();
        var beforeRelease = TimeProvider.System.GetTimestamp();
        scenario.Controls.WriteRelease(held.Arm, held.Marker.RequestId);
        var original = await held.Original.ConfigureAwait(false);
        await scenario.SettleAsync(held.Arm, held.Marker, token).ConfigureAwait(false);
        await ConnectionRf3FreshIdleWindowAssertions.ReceiptAsync(original.Value, command).ConfigureAwait(false);
        await ConnectionRf3PublicOperations.ReceiptAsync(original, command, held.Witness).ConfigureAwait(false);
        await scenario.VerifyDocumentAsync(identity, RequestCqrsRf3Protocol.ChangedDocumentJson,
            ConnectionRf3Protocol.UpdatedRevision, token).ConfigureAwait(false);
        await ConnectionRf3FreshIdleWindowAssertions.ObserveWindowAsync(scenario, held.Marker, beforeRelease, token)
            .ConfigureAwait(false);
        var closed = await ConnectionRf3WitnessReader.WaitAsync(scenario.Controls, held.Marker,
            scenario.Discovery, closed: true, cancellationToken: token).ConfigureAwait(false);
        await ConnectionRf3FreshIdleWindowAssertions.ClosedAsync(held.Witness, closed).ConfigureAwait(false);
        await ReplayAsync(scenario, command, original.Value, held.Witness, useMcp, token).ConfigureAwait(false);
    }

    private static async Task ReplayAsync(ConnectionRf3Scenario scenario, CommandRequest command,
        CommitReceipt original, ConnectionProbeWitness prior, bool useMcp, CancellationToken token)
    {
        var following = await scenario.CallerAsync(token).ConfigureAwait(false);
        var replay = await scenario.ObserveAsync(scenario.Identity, command.CommandId, null,
            () => ConnectionRf3PublicOperations.CommitAsync(following, command, useMcp, token), token).ConfigureAwait(false);
        await ConnectionRf3FreshIdleWindowAssertions.ReceiptAsync(replay.Result.Value, command).ConfigureAwait(false);
        await Assert.That(JsonDefaults.Serialize(replay.Result.Value).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(original))).IsTrue();
        await Assert.That(replay.Witness.ConnectionId).IsNotEqualTo(prior.ConnectionId);
        await Assert.That(replay.Witness.GrainId).IsNotEqualTo(prior.GrainId);
        await Assert.That(replay.Witness.ActivationId).IsNotEqualTo(prior.ActivationId);
        await ConnectionRf3PublicOperations.ReceiptAsync(replay.Result, command, replay.Witness).ConfigureAwait(false);
        await scenario.VerifyDocumentAsync(scenario.Identity, RequestCqrsRf3Protocol.ChangedDocumentJson,
            ConnectionRf3Protocol.UpdatedRevision, token).ConfigureAwait(false);
        await scenario.VerifyDocumentAsync(scenario.SecondIdentity, RequestCqrsRf3Protocol.DocumentJson,
            ConnectionRf3Protocol.InitialRevision, token).ConfigureAwait(false);
        var closed = await scenario.CloseAsync(following, replay.Marker, token).ConfigureAwait(false);
        await ConnectionRf3FreshIdleWindowAssertions.ClosedAsync(replay.Witness, closed).ConfigureAwait(false);
    }
}
