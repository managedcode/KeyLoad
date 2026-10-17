using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlParserAdmissionRf3Current
{
    internal static async Task ObserveAsync(RequestCqrsRf3Callers root, SqlParserAdmissionRf3State state, Func<Task> action, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(action, failures);
        await ServerFailureObserver.ObserveAsync(() => PureAsync(root, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task PureAsync(RequestCqrsRf3Callers root, SqlParserAdmissionRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var input in state.Original.Seed.Mutations.Cast<PutDocument>())
        {
            var reference = new EntityRef(state.Partition, input.Collection, input.Id);
            var expected = new DocumentReadResult(new(reference, 1L, input.Json, false, []));
            await ServerFailureObserver.ObserveAsync(async () =>
                await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(reference, token))), failures);
            await ServerFailureObserver.ObserveAsync(async () =>
                await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
                    await root.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value), failures);
        }
        var typed = new EntityRef(state.Partition, SqlParserAdmissionRf3State.Table, "typed-row");
        await ServerFailureObserver.ObserveAsync(async () =>
            await SqlRf3Protocol.EqualAsync(new DocumentReadResult(new(typed, 1L, SqlParserAdmissionRf3State.TypedJson, false, [])),
                await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(typed, token))), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await SqlRf3Protocol.EqualAsync(new DocumentReadResult(new(typed, 1L, SqlParserAdmissionRf3State.TypedJson, false, [])),
                (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(await root.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(typed), token))).Value), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var source = new StreamRef(state.Partition, SqlParserAdmissionRf3State.EventResource, "source");
            var stream = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadStreamAsync(new(source), token));
            await StreamAsync(stream, state.Partition);
            await SqlRf3Protocol.EqualAsync(state.Stream, stream with
            {
                CutPosition = 0L,
                SnapshotCutPosition = 0L
            });
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await SqlRf3Protocol.EqualAsync(state.Outbox, await QueryCursorRequestIdentityR47Current.OutboxAsync(root, state.Partition, token)), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task CheckAsync(RequestCqrsRf3Callers root, SqlParserAdmissionRf3State state, CancellationToken token)
    {
        await PureAsync(root, state, token);
        foreach (var (request, receipt) in new[] { (state.Original.Seed, state.Original.Receipt), (state.Events, state.EventReceipt) })
        {
            await ReceiptAsync(root, state, request.CommandId, receipt, token);
            await ObserveAsync(root, state, async () =>
                await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(request, token))), token);
            await ObserveAsync(root, state, async () =>
                await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                    await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token))).Value), token);
        }
        await ReceiptAsync(root, state, state.Insert.CommandId!.Value, state.InsertReceipt, token);
        await ObserveAsync(root, state, async () =>
            await SqlRf3Protocol.EqualAsync(state.InsertReceipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(root.Sdk, state.Insert, token)), token);
        await ObserveAsync(root, state, async () =>
            await SqlRf3Protocol.EqualAsync(state.InsertReceipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(root.Mcp, state.Insert, token)), token);
    }
    private static async Task ReceiptAsync(RequestCqrsRf3Callers root, SqlParserAdmissionRf3State state, Guid command, CommitReceipt receipt, CancellationToken token)
    {
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(new(1, state.Partition), token));
        await Assert.That(receipt.CommandId).IsEqualTo(command);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        MutationReceipt[] expected = command == state.Events.CommandId ? [new("appendEvents", SqlParserAdmissionRf3State.EventResource, "source", 2L)]
            : command == state.Insert.CommandId ? [new("putDocument", SqlParserAdmissionRf3State.Table, "typed-row", 1L)]
            : [.. state.Original.Seed.Mutations.Cast<PutDocument>().Select(input => new MutationReceipt("putDocument", input.Collection, input.Id, 1L))];
        await SqlRf3Protocol.EqualAsync(expected, receipt.Mutations.ToArray());
    }
    internal static async Task StreamAsync(StreamPage page, PartitionRef partition)
    {
        var source = new StreamRef(partition, SqlParserAdmissionRf3State.EventResource, "source");
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await Assert.That(page.SnapshotCutPosition).IsEqualTo(page.CutPosition);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.HasMore).IsFalse();
        await SqlRf3Protocol.EqualAsync(source, page.Stream);
        await SqlRf3Protocol.EqualAsync(new StreamHead(2L, 1L, 1L), page.Head);
        await Assert.That(page.Events.Length).IsEqualTo(2);
        foreach (var (record, expected) in page.Events.Zip(new[] { ("created", "Created", "{\"value\":1}", 1L), ("updated", "Updated", "{\"value\":2}", 2L) }))
        {
            await SqlRf3Protocol.EqualAsync(source, record.Stream);
            await Assert.That(record.Revision).IsEqualTo(expected.Item4);
            await Assert.That(record.EventSequence).IsEqualTo(expected.Item4);
            await SqlRf3Protocol.EqualAsync(new EventData(expected.Item1, expected.Item2, expected.Item3, "{}"), record.Data);
            await Assert.That(record.RecordedAt).IsNotEqualTo(default(DateTimeOffset));
        }
    }
}
