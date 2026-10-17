using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveReadCancellationR49Flow
{
    internal static async Task RunAsync(LiveReadCancellationR49Owner owner, CancellationToken token)
    {
        var snapshot = await LiveReadCancellationR49Pages.SnapshotAsync(owner,
            LiveReadCancellationR49Protocol.InitialRevision, LiveReadCancellationR49Protocol.InitialJson,
            LiveReadCancellationR49Protocol.InitialTail, token);
        owner.Original = new(owner.Query, snapshot.Cursor);
        owner.Update = new(Guid.NewGuid(), owner.Identity.Partition,
            [new PutDocument(LiveReadCancellationR49Protocol.Collection, LiveReadCancellationR49Protocol.Document,
                global::System.Text.Json.JsonSerializer.Serialize(
                    new LiveReadCancellationR49Protocol.DocumentModel(LiveReadCancellationR49Protocol.CurrentValue), JsonDefaults.Options),
                LiveReadCancellationR49Protocol.InitialRevision,
                ExplicitReplacement: true)]);
        owner.Updated = await McpCallerAssertions.SdkSuccessAsync(await owner.Administrator.Sdk.CommitAsync(owner.Update, token));
        await LiveReadCancellationR49Current.ReceiptAsync(owner.CurrentReceipt, owner.Update.CommandId,
            LiveReadCancellationR49Protocol.CurrentRevision, owner);
        owner.Outbox = await McpCallerAssertions.SdkSuccessAsync(
            await owner.Administrator.Sdk.OutboxStatusAsync(owner.Identity.Partition, token));
        await Assert.That(owner.Outbox.Head.Tail).IsEqualTo(LiveReadCancellationR49Protocol.CurrentTail);
        await Assert.That(owner.Outbox.Head.FirstAvailable).IsEqualTo(1L);
        await Assert.That(owner.Outbox.Head.StoredRecords).IsEqualTo(LiveReadCancellationR49Protocol.CurrentTail);
        await Assert.That(owner.Outbox.Head.StoredBytes).IsGreaterThan(0L);
        await Assert.That(owner.Outbox.Consumers.IsEmpty).IsTrue();
        await LiveReadCancellationR49Current.RunAsync(owner, token);
        await LiveReadCancellationR49Held.RunAsync(owner, false, token);
        await LiveReadCancellationR49Held.RunAsync(owner, true, token);
        await OriginalsAsync(owner, token);
        await owner.Restart(token);
        await LiveReadCancellationR49Current.RunAsync(owner, token);
        await LiveReadCancellationR49Pages.HealthyAsync(owner, owner.Original, false, token);
        await OriginalsAsync(owner, token);
        var fresh = await LiveReadCancellationR49Pages.SnapshotAsync(owner,
            LiveReadCancellationR49Protocol.CurrentRevision, LiveReadCancellationR49Protocol.CurrentJson,
            LiveReadCancellationR49Protocol.CurrentTail, token);
        var current = new ReadLiveQueryRequest(owner.Query, fresh.Cursor);
        await owner.Restart(token);
        await LiveReadCancellationR49Current.RunAsync(owner, token);
        await LiveReadCancellationR49Pages.HealthyAsync(owner, current, true, token);
        await OriginalsAsync(owner, token);
    }

    private static async Task OriginalsAsync(LiveReadCancellationR49Owner owner, CancellationToken token)
    {
        foreach (var original in new[] { (owner.Seed, owner.SeedReceipt), (owner.Update, owner.CurrentReceipt) })
        {
            await SqlRf3Protocol.EqualAsync(original.Item2, await McpCallerAssertions.SdkSuccessAsync(
                await owner.Administrator.Sdk.CommitAsync(original.Item1, token)));
            await LiveReadCancellationR49Current.RunAsync(owner, token);
            await SqlRf3Protocol.EqualAsync(original.Item2, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await owner.Administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Item1, token))).Value);
            await LiveReadCancellationR49Current.RunAsync(owner, token);
        }
    }
}
