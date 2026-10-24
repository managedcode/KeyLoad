using KeyLoad.Client;
using KeyLoad.Core.Messaging;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class RemoteTransferRepairClusterPreparation
{
    internal static async Task<RemoteTransferRepairClusterOriginal> CreateAsync(ClusterFixture fixture,
        string subject, QueueTransferRepairStage stage, CancellationToken token)
    {
        var seed = await RemoteTransferCoordinatorClusterSetup.CreateAsync(fixture, subject, fullTarget: false, token);
        var principal = seed.Identity.Principal;
        // The configured coordinator advances every pending intent its subject can inspect. The transfer is created
        // while the source cannot be inspected, so the coordinator first discovers it with the stage denial persisted.
        await MessagingClusterIdentity.UpdateAsync(fixture, MessagingClusterIdentity.WithCapability(principal,
            seed.Scenario.SourceQueue, RemoteTransferRepairClusterProtocol.UninspectedSource), token);
        var deniedLane = stage == QueueTransferRepairStage.Accept ? seed.Scenario.DestinationQueue : seed.Scenario.SourceQueue;
        var denied = MessagingClusterIdentity.WithCapability(principal, deniedLane, Capability.QueueInspect) with
        {
            PolicyEpoch = checked(principal.PolicyEpoch + RemoteTransferRepairClusterProtocol.DeniedEpochOffset)
        };
        RemoteTransferRepairClusterOriginal? original = null;
        await RemoteTransferColdCallers.WithAsync(fixture, seed, async (sdk, mcp) =>
        {
            var created = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(seed.Create, token));
            await MessagingClusterIdentity.UpdateAsync(fixture, denied, token);
            var intent = await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, null, token);
            await Assert.That(intent.State).IsEqualTo(QueueTransferState.OutputPending);
            var hint = RemoteTransferCoordinatorClusterAssertions.Hint(seed, created, intent) with
            {
                RepairCeiling = RemoteTransferRepairClusterProtocol.Ceiling
            };
            var proof = stage == QueueTransferRepairStage.Complete ? await AcceptAsync(sdk, seed, intent, token) : null;
            var partition = stage == QueueTransferRepairStage.Accept ? seed.Scenario.DestinationPartition : seed.Scenario.SourcePartition;
            Mutation effect = stage == QueueTransferRepairStage.Accept ? new AcceptQueueTransfer(seed.Scenario.DestinationQueue, intent.IntentToken)
                : new CompleteQueueTransfer(seed.Scenario.SourceQueue, seed.TransferId, proof!.ReceiptToken);
            var failed = new CommandRequest(RemoteTransferRepairIdentity.CommandId(hint, stage), partition, [effect]);
            await QueueProducerClusterAssertions.DeniedAsync(await sdk.CommitAsync(failed, token), ErrorCode.PermissionDenied);
            await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, intent, token);
            await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, proof, token);
            await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, seed, proof is null ? null
                : RemoteTransferColdAssertions.Ready(seed, RemoteTransferRepairClusterProtocol.ReadySequence), token);
            original = new(seed, intent, hint, failed, stage, proof);
        }, token);
        return original ?? throw new InvalidOperationException(RemoteTransferRepairClusterProtocol.Missing);
    }

    private static async Task<QueueTransferReceiptInspection> AcceptAsync(KeyLoadClient sdk, RemoteTransferColdSeed seed,
        QueueTransferInspection intent, CancellationToken token)
    {
        var accepted = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(new(Guid.NewGuid(),
            seed.Scenario.DestinationPartition, [new AcceptQueueTransfer(seed.Scenario.DestinationQueue, intent.IntentToken)]), token));
        var proof = await PublicReadAssertions.ReceiptAsync(await sdk.InspectQueueTransferReceiptAsync(seed.ReceiptRequest, token))
            ?? throw new InvalidOperationException(RemoteTransferRepairClusterProtocol.Missing);
        await Assert.That(proof.TargetCommit).IsEqualTo(accepted.Token);
        return proof;
    }
}
