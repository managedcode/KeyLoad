using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.DatabaseComposition;

internal static class EventReferenceSelectSources
{
    internal static async Task VerifyAsync(RequestCqrsClusterCallers admin, EventReferenceSelectState state, CancellationToken token)
    {
        foreach (var (id, json) in new[] { ("c1", EventReferenceSelectProtocol.FirstJson), ("c2", EventReferenceSelectProtocol.HiddenJson) })
        {
            var reference = new EntityRef(state.Partition, EventReferenceSelectProtocol.Target, id);
            var actual = await PublicReadAssertions.DocumentAsync(await admin.Sdk.GetAsync(reference, token));
            await SqlClusterProtocol.EqualAsync(new DocumentResult(state.StoredTarget(id), 1, json, false, []), actual);
        }
        var missing = new EntityRef(state.Partition, EventReferenceSelectProtocol.Target, "m");
        var repaired = await PublicReadAssertions.DocumentAsync(await admin.Sdk.GetAsync(missing, token));
        await SqlClusterProtocol.EqualAsync(state.Repair is null ? null : new DocumentResult(state.StoredTarget(missing.Id), 1,
            EventReferenceSelectProtocol.RepairedJson, false, []), repaired);
        await StreamAsync(admin, state, state.OriginalStream, EventReferenceSelectProtocol.StreamSet, [
            new("z", "linked", EventReferenceSelectProtocol.Payload(state.Partition, "c1", 7)),
            new("a", "linked", EventReferenceSelectProtocol.Payload(state.Partition, "m", 7)),
            new("m", "linked", EventReferenceSelectProtocol.Payload(state.Partition, "c2", 7)),
            new("b", "linked", EventReferenceSelectProtocol.Payload(state.Partition, "c1", 2)),
            new("n", "unlinked", "{\"score\":7}")], 1, token);
        await StreamAsync(admin, state, state.OriginalInvalid, EventReferenceSelectProtocol.InvalidStreamSet,
            [new("wrong", "linked", EventReferenceSelectProtocol.Payload(state.Partition with { PartitionKey = "foreign" }, "c1", 7))],
            6, token);
        await SqlClusterProtocol.EqualAsync(state.Receipt, await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(state.Seed, token)));
        if (state.Repair is not null)
        {
            await SqlClusterProtocol.EqualAsync(state.RepairReceipt,
                await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(state.Repair, token)));
        }
    }

    private static async Task StreamAsync(RequestCqrsClusterCallers admin, EventReferenceSelectState state,
        StreamPage original, string streamSet, EventData[] data, long firstSequence, CancellationToken token)
    {
        var expectedStream = new StreamRef(state.Partition, streamSet, EventReferenceSelectProtocol.Stream, 1);
        var request = new ReadStreamRequest(expectedStream);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadStreamAsync(request, token));
        var official = (await McpCallerAssertions.SuccessAsync<StreamPage>(
            await admin.Mcp.CallAsync(McpCallerTools.StreamsRead, request, token))).Value;
        await SqlClusterProtocol.EqualAsync(sdk.Stream, official.Stream);
        await SqlClusterProtocol.EqualAsync(sdk.Head, official.Head);
        await SqlClusterProtocol.EqualAsync(sdk.Events, official.Events);
        await Assert.That(official.Cursor).IsNull();
        await Assert.That(sdk.Cursor).IsNull();
        await Assert.That(official.SnapshotCutPosition).IsGreaterThanOrEqualTo(state.Receipt.Token.Position);
        await Assert.That(sdk.SnapshotCutPosition).IsGreaterThanOrEqualTo(state.Receipt.Token.Position);
        await Assert.That(official.CutPosition).IsGreaterThanOrEqualTo(state.Receipt.Token.Position);
        await Assert.That(official.HasMore).IsFalse();
        await Assert.That(sdk.HasMore).IsFalse();
        await Assert.That(sdk.CutPosition).IsGreaterThanOrEqualTo(state.Receipt.Token.Position);
        await SqlClusterProtocol.EqualAsync(expectedStream, sdk.Stream);
        await SqlClusterProtocol.EqualAsync(expectedStream, original.Stream);
        await SqlClusterProtocol.EqualAsync(new StreamHead(data.Length, 1, 1), sdk.Head);
        await Assert.That(sdk.Events.Length).IsEqualTo(data.Length);
        await Assert.That(original.Events.Length).IsEqualTo(data.Length);
        for (var index = 0; index < data.Length; index++)
        {
            var record = sdk.Events[index];
            await SqlClusterProtocol.EqualAsync(new EventRecord(expectedStream, index + 1L,
                firstSequence + index, data[index], original.Events[index].RecordedAt), record);
            await SqlClusterProtocol.EqualAsync(original.Events[index], record);
            await Assert.That(record.RecordedAt).IsEqualTo(state.OriginalStream.Events[0].RecordedAt);
        }
    }
}
