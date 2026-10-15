using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class DocumentLiveProfileRf3Values
{
    internal static async Task ReceiptAsync(RequestCqrsRf3Callers root, CommandRequest command, MutationReceipt[] effects,
        CommitReceipt actual, CancellationToken token)
    {
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(new(1, command.Partition), token));
        await Assert.That(actual.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(actual.Token.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(actual.Token.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
        await Assert.That(actual.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(effects, actual.Mutations.ToArray());
    }
    internal static async Task<string> ImageAsync(RequestCqrsRf3Callers root, DocumentLiveProfileRf3State state, int phase, CancellationToken token)
    {
        var documents = new List<DocumentReadResult>();
        OutboxStatus? outbox = null;
        var failures = new List<Exception>();
        foreach (var id in new[] { "a", "b", "c", "hidden" })
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var result = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(state.Reference(id), token));
                var value = id switch
                {
                    "a" => 1,
                    "b" => 3,
                    "c" => 5,
                    _ => 99
                };
                var present = id == "hidden" || (id == "a" ? phase == 0 : id == "b" ? phase >= 1 : phase == 2);
                var expected = new DocumentReadResult(present ? new(state.Reference(id), 1L, DocumentLiveProfileRf3State.Json(value), false, []) : null);
                await SqlRf3Protocol.EqualAsync(expected, result);
                documents.Add(result);
            }, failures);
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Partition, token));
            var tail = phase == 0 ? 2L : phase == 1 ? 5L : 6L;
            await Assert.That(outbox.Head.Tail).IsEqualTo(tail);
            await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(1L);
            await Assert.That(outbox.Head.StoredRecords).IsEqualTo(tail);
            await Assert.That(outbox.Head.StoredBytes).IsGreaterThan(0L);
            await Assert.That(outbox.Consumers).IsEmpty();
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return JsonSerializer.Serialize(new
        {
            Documents = documents,
            Outbox = outbox
        }, JsonDefaults.Options);
    }
    internal static async Task ObserveAsync(RequestCqrsRf3Callers root, DocumentLiveProfileRf3State state, int phase,
        Func<Task> operation, CancellationToken token)
    {
        var image = await ImageAsync(root, state, phase, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(await ImageAsync(root, state, phase, token)).IsEqualTo(image), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task<long> CutAsync(RequestCqrsRf3Callers reader, DocumentLiveProfileRf3State state, int phase, CancellationToken token)
    {
        var page = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAstAsync(state.Query, token));
        await SqlRf3Protocol.EqualAsync(new QueryPage([.. DocumentLiveProfileRf3State.Rows(phase)], null, page.CutPosition, "bounded-full-scan"), page);
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        return page.CutPosition;
    }
    internal static async Task ReplayAsync(RequestCqrsRf3Callers root, DocumentLiveProfileRf3State state, CancellationToken token)
    {
        foreach (var entry in state.Commands)
        {
            await SqlRf3Protocol.EqualAsync(entry.Receipt, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(entry.Command, token)));
            await SqlRf3Protocol.EqualAsync(entry.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, entry.Command, token))).Value);
        }
    }
}
