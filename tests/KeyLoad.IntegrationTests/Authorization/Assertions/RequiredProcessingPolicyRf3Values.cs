using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class RequiredProcessingPolicyRf3Values
{
    internal static async Task ReceiptAsync(RequestCqrsRf3Callers root, RequiredProcessingPolicyRf3State state,
        Guid command, MutationReceipt[] mutations, CommitReceipt actual, CancellationToken token)
    {
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(new(1, state.Partition), token));
        await Assert.That(actual.CommandId).IsEqualTo(command);
        await Assert.That(actual.Token.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(state.Partition.AtomicPartitionId);
        await Assert.That(actual.Token.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
        await Assert.That(actual.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(mutations, actual.Mutations.ToArray());
    }
    internal static MessageInspection Expected(RequiredProcessingPolicyRf3State state, string id, int phase, bool projected)
    {
        var index = id == "first" ? 0 : 1;
        var leased = phase == 0 || phase == 2 && index == 1;
        var delivery = state.Deliveries[index];
        var metadata = new MessageMetadata(id, leased ? MessageState.Leased : MessageState.Acked, 1, leased ? 2L : 3L, index + 1L,
            null, null, leased ? state.Worker : null, 1L, leased ? delivery.LeaseUntil : null, 1L);
        return new(metadata, leased ? projected ? "{\"value\":1}" : state.Payload : null,
            leased ? projected ? "{\"trace\":\"one\"}" : state.Headers : null);
    }
    internal static async Task<string> ImageAsync(RequestCqrsRf3Callers root, RequiredProcessingPolicyRf3State state, int phase, CancellationToken token)
    {
        var values = new List<object>();
        var failures = new List<Exception>();
        foreach (var id in phase < 2 ? new[] { "first" } : new[] { "first", "next" })
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var message = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.InspectAsync(new(state.Lane, id), token));
                await SqlRf3Protocol.EqualAsync(new MessageInspectionResult(Expected(state, id, phase, false)), message);
                values.Add(message);
            }, failures);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var present = id == "first" ? phase >= 1 : phase >= 3;
                var expected = new DocumentReadResult(present ? new(state.Reference(id), 1L, RequiredProcessingPolicyRf3State.EffectJson(id), false, []) : null);
                var document = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(state.Reference(id), token));
                await SqlRf3Protocol.EqualAsync(expected, document);
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
    internal static async Task ObserveAsync(RequestCqrsRf3Callers root, RequiredProcessingPolicyRf3State state, int phase,
        Func<Task> operation, CancellationToken token)
    {
        var before = await ImageAsync(root, state, phase, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(await ImageAsync(root, state, phase, token)).IsEqualTo(before), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
