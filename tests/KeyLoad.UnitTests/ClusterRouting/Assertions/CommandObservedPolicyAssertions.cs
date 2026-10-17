using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.UnitTests.Features.Authorization;
using P = KeyLoad.UnitTests.Features.ClusterRouting.CommandObservedPolicyProtocol;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class CommandObservedPolicyAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonElement.DeepEquals(Project(expected), Project(actual))).IsTrue();

    private static JsonElement Project<T>(T value)
    {
        if (value is CommitReceipt receipt)
        {
            // CompositionReferences is part of the native receipt but deliberately absent from its public JSON.
            return JsonSerializer.SerializeToElement(new
            {
                Receipt = receipt,
                CompositionReferences = receipt.Mutations.Select(mutation => mutation.CompositionReferences).ToArray()
            }, JsonDefaults.Options);
        }
        return JsonSerializer.SerializeToElement(value, JsonDefaults.Options);
    }

    internal static OperationResult Outcome(CommandObservedPolicyScenario scenario, Guid commandId)
        => OutcomeStoreOracle.ReadPartition(scenario.Fixture.Database.Store,
            scenario.Fixture.Database.Partition, P.Worker, commandId)
            ?? throw new InvalidOperationException(P.Missing);

    internal static byte[] OutcomeBytes(CommandObservedPolicyScenario scenario, Guid commandId)
        => scenario.Fixture.Database.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.PartitionKey(
            scenario.Fixture.Database.Partition, P.Worker, commandId)))
            ?? throw new InvalidOperationException(P.Missing);

    internal static async Task LiteralAsync(CommandObservedPolicyScenario scenario, ReceiveRequest request,
        ReceiveResult actual, string messageId)
    {
        var fixture = scenario.Fixture;
        var delivery = await Assert.That(actual.Deliveries).HasSingleItem();
        await Assert.That(delivery.Token).IsNotEmpty();
        var entry = fixture.Source.Log.ReadEntry(actual.Token.Position)
            ?? throw new InvalidOperationException(P.Missing);
        var operation = entry.Operation ?? throw new InvalidOperationException(P.Missing);
        await Assert.That(operation.Kind).IsEqualTo(OperationKind.Receive);
        await Assert.That(operation.Id).IsEqualTo(request.RequestId);
        await Assert.That(operation.PrincipalId).IsEqualTo(P.Worker);
        _ = fixture.Source.Database.VerifyOperationAuthority(operation);
        await EqualAsync(request, NativeCommandPayload.Read<ReceiveRequest>(operation));
        var expected = new ReceiveResult(request.RequestId,
            [new(messageId, P.Payload, P.Headers, delivery.Token, P.Initial,
                operation.EvaluatedAt.AddSeconds(scenario.Resource.QueuePolicy.MaxLeaseSeconds),
                P.One, P.Initial)],
            new(fixture.Database.Store.Identity.Incarnation, fixture.Database.Partition.AtomicPartitionId,
                entry.Index, P.Initial));
        await EqualAsync(expected, actual);
    }

    internal static T Value<T>(GrainOperationReply reply)
        => NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span).Value is T result
            ? result : throw new InvalidOperationException(P.WrongReply);

    internal static byte[] NativePayloadBytes(CommandObservedPolicyScenario scenario, ReceiveResult actual)
        => scenario.Fixture.Source.Log.ReadEntry(actual.Token.Position)?.Operation?.NativePayload.ToArray()
            ?? throw new InvalidOperationException(P.Missing);

    internal static async Task UnchangedAsync(CommandObservedPolicyScenario scenario,
        Guid commandId, Func<Task> execute, CancellationToken token)
    {
        var fixture = scenario.Fixture;
        await fixture.Database.Coordinator.ReadBarrierAsync(token);
        var rows = TeamRowPolicyAssertions.Image(fixture.Database.Store);
        var identity = NativeSerialization.Serialize(fixture.Database.Store.Identity);
        var position = fixture.Database.Store.Position;
        var applied = fixture.Database.Database.LastApplied;
        var cut = fixture.Database.ReadJournalCut();
        var outcome = OutcomeBytes(scenario, commandId);
        await execute();
        await Assert.That(TeamRowPolicyAssertions.Image(fixture.Database.Store).SequenceEqual(rows, StringComparer.Ordinal)).IsTrue();
        await Assert.That(NativeSerialization.Serialize(fixture.Database.Store.Identity).SequenceEqual(identity)).IsTrue();
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(position);
        await Assert.That(fixture.Database.Database.LastApplied).IsEqualTo(applied);
        await Assert.That(fixture.Database.ReadJournalCut()).IsEqualTo(cut);
        await Assert.That(OutcomeBytes(scenario, commandId).SequenceEqual(outcome)).IsTrue();
    }
}
