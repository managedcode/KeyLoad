using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.FunctionalTests.Authorization;
using ManagedCode.Communication;
using P = KeyLoad.FunctionalTests.OrleansExecution.CommandObservedPolicyProtocol;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class CommandObservedPolicyAssertions
{
    // Messaging preserves header JSON content exactly (JsonData.ValidateContent), so the stored headers equal the enqueued text.
    private const string ExpectedStoredHeadersJson = P.Headers;

    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(JsonElement.DeepEquals(Project(expected), Project(actual))).IsTrue();

    private static JsonElement Project<T>(T value)
    {
        if (value is CommitReceipt receipt)
        {
            // CompositionReferences is part of the execution receipt but deliberately absent from its public JSON.
            return JsonSerializer.SerializeToElement(new
            {
                Receipt = receipt,
                CompositionReferences = receipt.Mutations.Select(mutation => mutation.CompositionReferences).ToArray()
            }, JsonDefaults.Options);
        }
        return JsonSerializer.SerializeToElement(value, JsonDefaults.Options);
    }

    internal static Result<object> Outcome(CommandObservedPolicyScenario scenario, Guid commandId)
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
        var entry = fixture.Database.ReadStoredOutcome(request.RequestId);
        await Assert.That(entry.CanonicalSequence).IsEqualTo(actual.Token.Position);
        var operation = entry.PreparedCommand;
        await Assert.That(operation.Value!.Kind).IsEqualTo(OperationKind.Receive);
        await Assert.That(operation.CommandId).IsEqualTo(request.RequestId);
        await Assert.That(operation.UserId!).IsEqualTo(P.Worker);
        _ = fixture.Database.Database.OwnCommand(operation);
        await EqualAsync(request, ((ReceiveRequest)(operation).Value!.Input));
        var expected = new ReceiveResult(request.RequestId,
            [new(messageId, P.Payload, ExpectedStoredHeadersJson, delivery.Token, P.Initial,
                new DateTimeOffset(operation.Timestamp).AddSeconds(scenario.Resource.QueuePolicy.MaxLeaseSeconds),
                P.One, P.Initial)],
            new(fixture.Database.Store.Identity.Incarnation, fixture.Database.Partition.AtomicPartitionId,
                entry.CanonicalSequence));
        await EqualAsync(expected, actual);
    }

    internal static T Value<T>(GrainOperationReply reply)
        => GrainReplyFactory.RequireValue(reply).Value is T result
            ? result : throw new InvalidOperationException(P.WrongReply);

    internal static byte[] RuntimePayloadBytes(CommandObservedPolicyScenario scenario, ReceiveResult actual)
        => OrleansSerialization.Serialize(scenario.Fixture.Database.ReadStoredOutcome(actual.RequestId).PreparedCommand.Value!.Input, ValidationProfile.PublicInputElements);

    internal static async Task UnchangedAsync(CommandObservedPolicyScenario scenario,
        Guid commandId, Func<Task> execute, CancellationToken token)
    {
        var fixture = scenario.Fixture;
        await fixture.Database.Coordinator.ReadBarrierAsync(token);
        var rows = TeamRowPolicyAssertions.Image(fixture.Database.Store);
        var identity = OrleansSerialization.Serialize(fixture.Database.Store.Identity);
        var position = fixture.Database.Store.Position;
        var applied = fixture.Database.Database.LastApplied;
        var cut = fixture.Database.ReadCanonicalCut();
        var outcome = OutcomeBytes(scenario, commandId);
        await execute();
        await Assert.That(TeamRowPolicyAssertions.Image(fixture.Database.Store).SequenceEqual(rows, StringComparer.Ordinal)).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(fixture.Database.Store.Identity).SequenceEqual(identity)).IsTrue();
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(position);
        await Assert.That(fixture.Database.Database.LastApplied).IsEqualTo(applied);
        await Assert.That(fixture.Database.ReadCanonicalCut()).IsEqualTo(cut);
        await Assert.That(OutcomeBytes(scenario, commandId).SequenceEqual(outcome)).IsTrue();
    }
}
