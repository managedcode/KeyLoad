using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferPostAwaitContinuation
{
    internal static async Task ExecuteAsync(PartitionMovementLateNativeOwners owners,
        RemoteTransferColdSeed seed, QueueTransferInspection intent, CommandRequest accept,
        CancellationToken token)
    {
        var native = RemoteTransferPostAwaitNativeCut.Read(owners, seed, accept);
        QueueTransferReceiptInspection proof;
        await using (var calls = new RemoteTransferPostAwaitCallers(new(owners.Settings.Origin(0)), seed.Identity.Secret))
        {
            await calls.InitializeAsync(seed.Identity.Secret, token);
            proof = await RemoteTransferPostAwaitAssertions.ReceiptAsync(calls, seed, native.OriginalReceipt, token);
            await RequireAcceptedAsync(calls, seed, intent, accept, native, proof, token);
        }
        await ColdAsync(owners, token);
        CommandRequest complete;
        CommitReceipt completed;
        await using (var calls = new RemoteTransferPostAwaitCallers(new(owners.Settings.Origin(0)), seed.Identity.Secret))
        {
            await calls.InitializeAsync(seed.Identity.Secret, token);
            await native.RequireAsync(RemoteTransferPostAwaitNativeCut.Read(owners, seed, accept));
            await RequireAcceptedAsync(calls, seed, intent, accept, native, proof, token);
            complete = seed.Complete(proof);
            completed = await McpCallerAssertions.SdkSuccessAsync(await calls.Sdk.CommitAsync(complete, token));
            await RemoteTransferColdAssertions.ReceiptLiteralAsync(completed, complete, seed.Scenario.SourceQueue,
                seed.TransferId, RemoteTransferColdProtocol.CompleteKind, RemoteTransferColdProtocol.CompleteRevision);
            await calls.ReplayAsync(complete, completed, token);
        }
        await ColdAsync(owners, token);
        await using var after = new RemoteTransferPostAwaitCallers(new(owners.Settings.Origin(0)), seed.Identity.Secret);
        await after.InitializeAsync(seed.Identity.Secret, token);
        await native.RequireAsync(RemoteTransferPostAwaitNativeCut.Read(owners, seed, accept));
        await after.ReplayAsync(accept, native.OriginalReceipt, token);
        await after.ReplayAsync(complete, completed, token);
        await RemoteTransferPostAwaitAssertions.OldCreateDeniedAsync(after, seed, token);
        await RemoteTransferPostAwaitAssertions.StateAsync(after, seed,
            intent with { State = QueueTransferState.Delivered, ReceiptToken = proof.ReceiptToken }, proof,
            RemoteTransferColdAssertions.Ready(seed, RemoteTransferColdProtocol.OriginalReadySequence), token);
        await HealthyAsync(after, seed, token);
        await after.ReplayAsync(accept, native.OriginalReceipt, token);
        await after.ReplayAsync(complete, completed, token);
        await RemoteTransferPostAwaitAssertions.StateAsync(after, seed,
            intent with { State = QueueTransferState.Delivered, ReceiptToken = proof.ReceiptToken }, proof,
            RemoteTransferColdAssertions.Ready(seed, RemoteTransferColdProtocol.OriginalReadySequence), token);
    }

    private static async Task RequireAcceptedAsync(RemoteTransferPostAwaitCallers calls, RemoteTransferColdSeed seed,
        QueueTransferInspection intent, CommandRequest accept, RemoteTransferPostAwaitNativeCut native,
        QueueTransferReceiptInspection proof, CancellationToken token)
    {
        await calls.ReplayAsync(accept, native.OriginalReceipt, token);
        await RemoteTransferPostAwaitAssertions.OldCreateDeniedAsync(calls, seed, token);
        await RemoteTransferPostAwaitAssertions.StateAsync(calls, seed, intent, proof,
            RemoteTransferColdAssertions.Ready(seed, RemoteTransferColdProtocol.OriginalReadySequence), token);
    }

    private static async Task ColdAsync(PartitionMovementLateNativeOwners owners, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await owners.StopAsync();
        token.ThrowIfCancellationRequested();
        await owners.StartAsync(token);
    }

    private static async Task HealthyAsync(RemoteTransferPostAwaitCallers calls, RemoteTransferColdSeed original,
        CancellationToken token)
    {
        var message = original.Message with
        {
            MessageId = RemoteTransferColdProtocol.Healthy,
            PayloadJson = RemoteTransferColdProtocol.HealthyPayload
        };
        var id = Guid.NewGuid();
        var seed = original with
        {
            TransferId = id,
            Message = message,
            Create = new(Guid.NewGuid(), original.Scenario.SourcePartition,
                [new CreateQueueTransfer(original.Scenario.SourceQueue, id, original.Scenario.DestinationQueue, message)])
        };
        var created = await McpCallerAssertions.SdkSuccessAsync(await calls.Sdk.CommitAsync(seed.Create, token));
        await calls.ReplayAsync(seed.Create, created, token);
        var intent = await McpCallerAssertions.SdkSuccessAsync(await calls.Sdk.InspectQueueTransferAsync(seed.SourceRequest, token))
            ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        var accept = seed.Accept(intent);
        var accepted = await McpCallerAssertions.SdkSuccessAsync(await calls.Sdk.CommitAsync(accept, token));
        await calls.ReplayAsync(accept, accepted, token);
        var proof = await RemoteTransferPostAwaitAssertions.ReceiptAsync(calls, seed, accepted, token);
        var complete = seed.Complete(proof);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await calls.Sdk.CommitAsync(complete, token));
        await calls.ReplayAsync(complete, completed, token);
        await RemoteTransferPostAwaitAssertions.StateAsync(calls, seed,
            intent with { State = QueueTransferState.Delivered, ReceiptToken = proof.ReceiptToken }, proof,
            RemoteTransferColdAssertions.Ready(seed, RemoteTransferColdProtocol.HealthyReadySequence), token);
    }
}
