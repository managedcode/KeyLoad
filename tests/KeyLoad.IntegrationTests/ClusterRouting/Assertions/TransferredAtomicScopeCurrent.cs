using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TransferredAtomicScopeCurrent
{
    private const string PutKind = "putDocument";
    internal static async Task RequireReceiptAsync(CommandRequest command, CommitReceipt receipt,
        DocumentResult document, Guid incarnation, long ownershipEpoch)
    {
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        var expected = new CommitReceipt(command.CommandId,
            new(incarnation, command.Partition.AtomicPartitionId, receipt.Token.Position, ownershipEpoch),
            [new(PutKind, document.Reference.Collection, document.Reference.Id, document.Revision)],
            DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(expected, receipt);
    }
    internal static async Task<OutboxStatus> OutboxAsync(PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var source = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.OutboxStatusAsync(seed.Partition, token));
        var target = await McpCallerAssertions.SdkSuccessAsync(await seed.Target.OutboxStatusAsync(seed.Partition, token));
        var official = (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await seed.Official.CallAsync(
            McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(seed.Partition), token))).Value;
        await SqlRf3Protocol.EqualAsync(source, target);
        await SqlRf3Protocol.EqualAsync(source, official);
        return source;
    }
    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        TransferredAtomicScopeState state, CancellationToken token)
    {
        await PartitionMovementPublicParentRf3Cut.RequireCurrentModelsAsync(seed, token);
        var reference = new EntityRef(seed.Partition, RelationalSqlRf3Tokens.Table, TransferredAtomicScopeCommands.RefusedRow);
        foreach (var sdk in new[] { seed.Source, seed.Target })
        {
            await Assert.That(await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(reference, token))).IsNull();
            await Assert.That(await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(
                seed.Models.Inspect(TransferredAtomicScopeCommands.RefusedMessage), token))).IsNull();
            if (state.HealthyDocument is { } healthy)
            {
                await SqlRf3Protocol.EqualAsync(healthy, await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(healthy.Reference, token)));
            }
        }
        await Assert.That((await PublicReadAssertions.DocumentAsync(await seed.Official.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value).IsNull();
        await Assert.That((await PublicReadAssertions.MessageAsync(await seed.Official.CallAsync(
            McpCallerTools.MessagesInspect, seed.Models.Inspect(TransferredAtomicScopeCommands.RefusedMessage), token))).Value).IsNull();
        if (state.HealthyDocument is { } expected)
        {
            await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(await seed.Official.CallAsync(
                McpCallerTools.DocumentsGet, new GetDocumentRequest(expected.Reference), token))).Value);
        }
        await SqlRf3Protocol.EqualAsync(state.Outbox, await OutboxAsync(seed, token));
    }
    internal static async Task<TransferredAtomicScopeState> AfterHealthyAsync(PartitionMovementPublicParentRf3Seed seed,
        TransferredAtomicScopeState original, DocumentResult expected, CancellationToken token)
    {
        var current = await OutboxAsync(seed, token);
        await Assert.That(current.Head.Tail).IsEqualTo(checked(original.Outbox.Head.Tail + 1));
        await Assert.That(current.Head.StoredRecords).IsEqualTo(checked(original.Outbox.Head.StoredRecords + 1));
        await Assert.That(current.Head.FirstAvailable).IsEqualTo(original.Outbox.Head.FirstAvailable);
        await Assert.That(current.Head.StoredBytes).IsGreaterThan(original.Outbox.Head.StoredBytes);
        await SqlRf3Protocol.EqualAsync(original.Outbox.Consumers, current.Consumers);
        var next = original with
        {
            Outbox = current,
            HealthyDocument = expected
        };
        await RequireAsync(seed, next, token);
        return next;
    }
}
