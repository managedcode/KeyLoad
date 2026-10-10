using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferDistinctStages
{
    internal static async Task ExecuteAsync(TwoRf3MembershipWave wave, RemoteTransferDistinctSeed setup,
        PartitionMovementPublicParentRf3Seed parent, CancellationToken token)
    {
        var seed = setup.Transfer;
        (CommitReceipt Receipt, QueueTransferInspection Intent)? original = null;
        await RemoteTransferDistinctCallers.WithAsync(wave, seed, async (sdk, mcp) =>
        { original = await RemoteTransferColdStages.CreateAsync(sdk, mcp, seed, token); }, token);
        var created = original ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        var accept = seed.Accept(created.Intent);
        (CommitReceipt Receipt, QueueTransferReceiptInspection Proof)? accepted = null;
        await RemoteTransferDistinctCallers.WithAsync(wave, seed, async (sdk, mcp) =>
        {
            accepted = await RemoteTransferColdStages.AcceptAsync(sdk, mcp, seed, created.Intent, accept,
                RemoteTransferColdProtocol.OriginalReadySequence, token);
        }, token);
        var target = accepted ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        var destination = parent.Directory.Owners.Single(value => value.Owner.PhysicalShardId
            != parent.Directory.ControlOwner.PhysicalShardId).Owner;
        await Assert.That(target.Receipt.Token.Incarnation).IsEqualTo(destination.Incarnation);
        await Assert.That(target.Receipt.Token.Incarnation).IsNotEqualTo(created.Receipt.Token.Incarnation);
        await RemoteTransferDistinctPolicy.RepairAsync(wave, parent, seed, created.Intent, accept, target.Proof, token);
        await RequireAcceptedAsync(wave, seed, created.Intent, accept, target, token);
        await ColdAsync(wave, token);
        await RequireAcceptedAsync(wave, seed, created.Intent, accept, target, token);
        var complete = seed.Complete(target.Proof);
        CommitReceipt? completed = null;
        await RemoteTransferDistinctCallers.WithAsync(wave, seed, async (sdk, mcp) =>
        {
            completed = await RemoteTransferColdStages.CompleteAsync(sdk, mcp, seed, created.Intent,
            target.Proof, complete, token);
        }, token);
        ArgumentNullException.ThrowIfNull(completed);
        await Assert.That(completed.Token.Incarnation).IsEqualTo(created.Receipt.Token.Incarnation);
        await ColdAsync(wave, token);
        await RemoteTransferDistinctCallers.WithAsync(wave, seed, async (sdk, mcp) =>
        {
            await RemoteTransferColdRefusals.OriginalEpochRefusedAsync(sdk, mcp, seed, token);
            await QueueProducerRf3Assertions.ReplayAsync(sdk, mcp, accept, target.Receipt, token);
            await QueueProducerRf3Assertions.ReplayAsync(sdk, mcp, complete, completed, token);
            await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, created.Intent with
            { State = QueueTransferState.Delivered, ReceiptToken = target.Proof.ReceiptToken }, token);
            await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, target.Proof, token);
            await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, seed,
                RemoteTransferColdAssertions.Ready(seed, RemoteTransferColdProtocol.OriginalReadySequence), token);
        }, token);
        await RemoteTransferDistinctAck.ExecuteAsync(wave, setup, created.Intent, accept, target, token);
        await RemoteTransferDistinctHealthy.ExecuteAsync(wave, seed, token);
        await parent.VerifyAsync(token);
    }

    private static async Task RequireAcceptedAsync(TwoRf3MembershipWave wave, RemoteTransferColdSeed seed,
        QueueTransferInspection intent, CommandRequest accept,
        (CommitReceipt Receipt, QueueTransferReceiptInspection Proof) target, CancellationToken token)
        => await RemoteTransferDistinctCallers.WithAsync(wave, seed, async (sdk, mcp) =>
        {
            await RemoteTransferColdRefusals.OriginalEpochRefusedAsync(sdk, mcp, seed, token);
            await QueueProducerRf3Assertions.ReplayAsync(sdk, mcp, accept, target.Receipt, token);
            await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, intent, token);
            await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, target.Proof, token);
            await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, seed,
                RemoteTransferColdAssertions.Ready(seed, RemoteTransferColdProtocol.OriginalReadySequence), token);
        }, token);

    private static async Task ColdAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _ = await wave.StopForDirectoryReadAsync();
        token.ThrowIfCancellationRequested();
        await wave.RestartJoinedAsync(token);
    }
}
