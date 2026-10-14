using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferNativeContinuation
{
    internal static async Task ExecuteAsync(TwoRf3MembershipWave wave, RemoteTransferNativeAdministrator administrator,
        RemoteTransferColdSeed seed, QueueTransferInspection intent, CommandRequest accept,
        NativeCanonicalModelCutRequest scope, RemoteModelNativeRequest original,
        RemoteTransferPostAwaitNativeCut native, CancellationToken token)
    {
        QueueTransferReceiptInspection proof;
        await using (var calls = await ConnectAsync(wave, seed, token))
        {
            proof = await RemoteTransferNativeStateAssertions.ReceiptAsync(calls, seed, native.OriginalReceipt, token);
            await RequireAcceptedAsync(calls, seed, intent, accept, native, proof, token);
        }
        var before = await RemoteTransferNativeCutLifecycle.LiveAsync(wave, scope, token);
        await RemoteTransferNativeCutLifecycle.RequireTargetAsync(before, seed, accept, original, native);
        await RemoteTransferNativeCutLifecycle.ColdAsync(wave, administrator, scope, before, token);
        CommandRequest complete;
        CommitReceipt completed;
        await using (var calls = await ConnectAsync(wave, seed, token))
        {
            await RequireAcceptedAsync(calls, seed, intent, accept, native, proof, token);
            var replayed = await RemoteTransferNativeCutLifecycle.LiveAsync(wave, scope, token);
            RemoteTransferNativeCutLifecycle.RequireUnchanged(before, replayed, token);
            complete = seed.Complete(proof);
            completed = await McpCallerAssertions.SdkSuccessAsync(await calls.Sdk.CommitAsync(complete, token));
            await RemoteTransferColdAssertions.ReceiptLiteralAsync(completed, complete, seed.Scenario.SourceQueue,
                seed.TransferId, RemoteTransferColdProtocol.CompleteKind, RemoteTransferColdProtocol.CompleteRevision);
            await calls.ReplayAsync(complete, completed, token);
        }
        var delivered = await RemoteTransferNativeCutLifecycle.LiveAsync(wave, scope, token);
        await RemoteTransferNativeCutLifecycle.RequireTargetAsync(delivered, seed, accept, original, native);
        await RemoteTransferNativeCutLifecycle.ColdAsync(wave, administrator, scope, delivered, token);
        await using var after = await ConnectAsync(wave, seed, token);
        await after.ReplayAsync(accept, native.OriginalReceipt, token);
        await after.ReplayAsync(complete, completed, token);
        await RemoteTransferNativeStateAssertions.OldCreateDeniedAsync(after, seed, token);
        await RequireDeliveredAsync(after, seed, intent, proof, token);
        var coldReplayed = await RemoteTransferNativeCutLifecycle.LiveAsync(wave, scope, token);
        RemoteTransferNativeCutLifecycle.RequireUnchanged(delivered, coldReplayed, token);
        await HealthyAsync(after, seed, token);
        var healthy = await RemoteTransferNativeCutLifecycle.LiveAsync(wave, scope, token);
        await after.ReplayAsync(accept, native.OriginalReceipt, token);
        await after.ReplayAsync(complete, completed, token);
        await RequireDeliveredAsync(after, seed, intent, proof, token);
        RemoteTransferNativeCutLifecycle.RequireUnchanged(healthy,
            await RemoteTransferNativeCutLifecycle.LiveAsync(wave, scope, token), token);
    }

    private static Task<RemoteTransferNativeCallers> ConnectAsync(TwoRf3MembershipWave wave,
        RemoteTransferColdSeed seed, CancellationToken token)
        => RemoteTransferNativeCallers.ConnectAsync(wave, TwoRf3MembershipProtocol.Nodes[0], seed.Identity.Secret, token);

    private static async Task RequireAcceptedAsync(RemoteTransferNativeCallers calls, RemoteTransferColdSeed seed,
        QueueTransferInspection intent, CommandRequest accept, RemoteTransferPostAwaitNativeCut native,
        QueueTransferReceiptInspection proof, CancellationToken token)
    {
        await calls.ReplayAsync(accept, native.OriginalReceipt, token);
        await RemoteTransferNativeStateAssertions.OldCreateDeniedAsync(calls, seed, token);
        await RemoteTransferNativeStateAssertions.StateAsync(calls, seed, intent, proof,
            RemoteTransferColdAssertions.Ready(seed, RemoteTransferColdProtocol.OriginalReadySequence), token);
    }

    private static Task RequireDeliveredAsync(RemoteTransferNativeCallers calls, RemoteTransferColdSeed seed,
        QueueTransferInspection intent, QueueTransferReceiptInspection proof, CancellationToken token)
        => RemoteTransferNativeStateAssertions.StateAsync(calls, seed,
            intent with
            {
                State = QueueTransferState.Delivered,
                ReceiptToken = proof.ReceiptToken
            }, proof,
            RemoteTransferColdAssertions.Ready(seed, RemoteTransferColdProtocol.OriginalReadySequence), token);

    private static async Task HealthyAsync(RemoteTransferNativeCallers calls, RemoteTransferColdSeed original,
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
        var intent = await PublicReadAssertions.TransferAsync(await calls.Sdk.InspectQueueTransferAsync(seed.SourceRequest, token))
            ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        var accept = seed.Accept(intent);
        var accepted = await McpCallerAssertions.SdkSuccessAsync(await calls.Sdk.CommitAsync(accept, token));
        await calls.ReplayAsync(accept, accepted, token);
        var proof = await RemoteTransferNativeStateAssertions.ReceiptAsync(calls, seed, accepted, token);
        var complete = seed.Complete(proof);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await calls.Sdk.CommitAsync(complete, token));
        await calls.ReplayAsync(complete, completed, token);
        await RemoteTransferNativeStateAssertions.StateAsync(calls, seed,
            intent with
            {
                State = QueueTransferState.Delivered,
                ReceiptToken = proof.ReceiptToken
            }, proof,
            RemoteTransferColdAssertions.Ready(seed, RemoteTransferColdProtocol.HealthyReadySequence), token);
    }
}
