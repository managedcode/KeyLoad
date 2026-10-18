using static KeyLoad.UnitTests.Features.QueryExecution.QueueViewLeaseIsolationProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationSource
{
    internal static Task RequireAsync(QueueViewLeaseIsolationState state)
        => QueueViewLeaseIsolationReads.ObserveAsync(state, async () =>
        {
            var producer = state.History.First(item => item.Operation.Value!.Kind == OperationKind.Batch);
            var stream = new StreamRef(state.Partition, Events, QueueViewLeaseIsolationProtocol.Stream);
            var actual = state.Owner.Database.ReadStream(Root, new ReadStreamRequest(stream, Limit: RowLimit),
                cancellationToken: state.Token);
            var expected = new StreamPage(stream, new(SecondSequence, Initial, Initial),
                [new(stream, Initial, Initial, new(FirstEvent, CreatedType, FirstPayload, FirstHeaders), new DateTimeOffset(producer.Operation.Timestamp)),
                 new(stream, SecondSequence, SecondSequence, new(SecondEvent, PaidType, SecondPayload, SecondHeaders),
                    new DateTimeOffset(producer.Operation.Timestamp))], state.Owner.Store.Position, false, null, state.Owner.Store.Position);
            await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        });
}
