using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.Authorization;

internal static class RequiredProcessingPolicyClusterValues
{
    internal static async Task ReceiptAsync(RequiredProcessingPolicyClusterState state,
        Guid command, MutationReceipt[] mutations, CommitReceipt actual)
    {
        await Assert.That(actual.CommandId).IsEqualTo(command);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(state.Partition.AtomicPartitionId);
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
        await Assert.That(actual.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(actual.PersistedCopies).IsEqualTo(3);
        await SqlClusterProtocol.EqualAsync(mutations, actual.Mutations.ToArray());
    }
    internal static MessageInspection Expected(RequiredProcessingPolicyClusterState state, string id, int phase, bool projected)
    {
        var index = id == "first" ? 0 : 1;
        var leased = phase == 0 || phase == 2 && index == 1;
        var delivery = state.Deliveries[index];
        var metadata = new MessageMetadata(id, leased ? MessageState.Leased : MessageState.Acked, 1, leased ? 2L : 3L, index + 1L,
            null, null, leased ? state.Worker : null, 1L, leased ? delivery.LeaseUntil : null, 1L);
        return new(metadata, leased ? projected ? "{\"value\":1}" : state.Payload : null,
            leased ? projected ? "{\"trace\":\"one\"}" : state.Headers : null);
    }
    internal static async Task<string> ImageAsync(RequestCqrsClusterCallers root, RequiredProcessingPolicyClusterState state, int phase, CancellationToken token)
    {
        var values = new List<object>();
        var failures = new List<Exception>();
        foreach (var id in phase < 2 ? new[] { "first" } : new[] { "first", "next" })
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var message = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.InspectAsync(new(state.Lane, id), token));
                await SqlClusterProtocol.EqualAsync(new MessageInspectionResult(Expected(state, id, phase, false)), message);
                values.Add(message);
            }, failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var present = id == "first" ? phase >= 1 : phase >= 3;
                var expected = new DocumentReadResult(present ? new(state.StoredReference(id), 1L, RequiredProcessingPolicyClusterState.EffectJson(id), false, []) : null);
                var document = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(state.Reference(id), token));
                await SqlClusterProtocol.EqualAsync(expected, document);
                values.Add(document);
            }, failures);
        }
        OutboxStatus? outbox = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Partition, token));
            var tail = phase + 1L;
            await Assert.That(outbox.Head.Tail).IsEqualTo(tail);
            await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(1L);
            await Assert.That(outbox.Head.StoredRecords).IsEqualTo(tail);
            await Assert.That(outbox.Head.StoredBytes).IsGreaterThan(0L);
            await Assert.That(outbox.Consumers).IsEmpty();
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return JsonSerializer.Serialize(new
        {
            Values = values,
            Outbox = outbox
        }, JsonDefaults.Options);
    }
    internal static async Task ObserveAsync(RequestCqrsClusterCallers root, RequiredProcessingPolicyClusterState state, int phase,
        Func<Task> operation, CancellationToken token)
    {
        var before = await ImageAsync(root, state, phase, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(await ImageAsync(root, state, phase, token)).IsEqualTo(before), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
