using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class InitialPrincipalEpochRf3Values
{
    internal static async Task ReceiptAsync(RequestCqrsRf3Callers root, CommandRequest command, CommitReceipt actual,
        long revision, CancellationToken token)
    {
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(new(1, command.Partition), token));
        await Assert.That(actual.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(actual.Token.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
        await Assert.That(actual.Token.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
        await Assert.That(actual.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync<MutationReceipt[]>([new("putDocument", InitialPrincipalEpochRf3State.Collection, "record", revision)],
            actual.Mutations.ToArray());
    }
    internal static async Task<string> ImageAsync(RequestCqrsRf3Callers root, InitialPrincipalEpochRf3State state, long revision, CancellationToken token)
    {
        DocumentReadResult? document = null;
        OutboxStatus? outbox = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            document = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(state.Reference, token));
            await SqlRf3Protocol.EqualAsync(new DocumentReadResult(new(state.Reference, revision,
                revision == 1L ? "{\"value\":0}" : "{\"value\":7}", false, [])), document);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Partition, token));
            await Assert.That(outbox.Head.Tail).IsEqualTo(revision);
            await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(1L);
            await Assert.That(outbox.Head.StoredRecords).IsEqualTo(revision);
            await Assert.That(outbox.Head.StoredBytes).IsGreaterThan(0L);
            await Assert.That(outbox.Consumers).IsEmpty();
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            Document = document,
            Outbox = outbox
        }, JsonDefaults.Options);
    }
    internal static async Task ObserveAsync(RequestCqrsRf3Callers root, InitialPrincipalEpochRf3State state,
        long revision, Func<Task> operation, CancellationToken token)
    {
        var image = await ImageAsync(root, state, revision, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(await ImageAsync(root, state, revision, token)).IsEqualTo(image), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task DocumentAsync(RequestCqrsRf3Callers reader, InitialPrincipalEpochRf3State state,
        long revision, CancellationToken token)
    {
        var expected = new DocumentReadResult(new(state.Reference, revision, revision == 1L ? "{\"value\":0}" : "{\"value\":7}", false, []));
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.GetAsync(state.Reference, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(
            await reader.Mcp.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(state.Reference), token))).Value);
    }
}
