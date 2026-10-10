using KeyLoad.Client;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferRepairRf3Preparation
{
    internal static async Task<RemoteTransferRepairRf3Original> CreateAsync(ClusterFixture fixture,
        string subject, QueueTransferRepairStage stage, CancellationToken token)
    {
        var seed = await RemoteTransferCoordinatorRf3Setup.CreateAsync(fixture, subject, fullTarget: false, token);
        RemoteTransferRepairRf3Original? original = null;
        await RemoteTransferColdCallers.WithAsync(fixture, seed, async (sdk, mcp) =>
        {
            var created = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(seed.Create, token));
            var intent = await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, null, token);
            await Assert.That(intent.State).IsEqualTo(QueueTransferState.OutputPending);
            var hint = RemoteTransferCoordinatorRf3Assertions.Hint(seed, created, intent) with
            { RepairCeiling = RemoteTransferRepairRf3Protocol.Ceiling };
            var deniedLane = stage == QueueTransferRepairStage.Accept ? seed.Scenario.DestinationQueue : seed.Scenario.SourceQueue;
            await MessagingRf3Identity.UpdateAsync(fixture, MessagingRf3Identity.WithCapability(seed.Identity.Principal,
                deniedLane, Capability.QueueInspect), token);
            QueueTransferReceiptInspection? proof = null;
            if (stage == QueueTransferRepairStage.Complete)
            {
                var accepted = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(new(Guid.NewGuid(),
                    seed.Scenario.DestinationPartition, [new AcceptQueueTransfer(seed.Scenario.DestinationQueue, intent.IntentToken)]), token));
                proof = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectQueueTransferReceiptAsync(seed.ReceiptRequest, token))
                    ?? throw new InvalidOperationException(RemoteTransferRepairRf3Protocol.Missing);
                await Assert.That(proof.TargetCommit).IsEqualTo(accepted.Token);
            }
            var partition = stage == QueueTransferRepairStage.Accept ? seed.Scenario.DestinationPartition : seed.Scenario.SourcePartition;
            Mutation effect = stage == QueueTransferRepairStage.Accept ? new AcceptQueueTransfer(seed.Scenario.DestinationQueue, intent.IntentToken)
                : new CompleteQueueTransfer(seed.Scenario.SourceQueue, seed.TransferId, proof!.ReceiptToken);
            var failed = new CommandRequest(RemoteTransferRepairIdentity.CommandId(hint, stage), partition, [effect]);
            await QueueProducerRf3Assertions.DeniedAsync(await sdk.CommitAsync(failed, token), ErrorCode.PermissionDenied);
            await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, intent, token);
            await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, proof, token);
            await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, seed, proof is null ? null
                : RemoteTransferColdAssertions.Ready(seed, RemoteTransferRepairRf3Protocol.ReadySequence), token);
            original = new(seed, intent, hint, failed, stage, proof);
        }, token);
        return original ?? throw new InvalidOperationException(RemoteTransferRepairRf3Protocol.Missing);
    }
}
