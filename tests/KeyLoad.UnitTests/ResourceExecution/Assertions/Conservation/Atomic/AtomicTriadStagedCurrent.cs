using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class AtomicTriadStagedCurrent
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(actual, JsonDefaults.Options),
            JsonSerializer.SerializeToElement(expected, JsonDefaults.Options))).IsTrue();

    internal static async Task EmptyAsync(AtomicTriadStagedFixture state)
    {
        await Assert.That(state.Owner.GetDocument(AtomicTriadStagedProtocol.Root,
            new(state.Partition, AtomicTriadStagedProtocol.Collection, AtomicTriadStagedProtocol.Entity))).IsNull();
        var page = state.Owner.ReadStream(AtomicTriadStagedProtocol.Root, new StreamRef(state.Partition,
            AtomicTriadStagedProtocol.Streams, AtomicTriadStagedProtocol.Entity));
        await EqualAsync(page.Head, new StreamHead(0L, 1L, 1L));
        await Assert.That(page.Events).IsEmpty();
        foreach (var id in new[] { AtomicTriadStagedProtocol.FirstMessage, AtomicTriadStagedProtocol.SecondMessage })
        {
            await Assert.That(state.Owner.InspectMessage(AtomicTriadStagedProtocol.Root, new(state.Partition, AtomicTriadStagedProtocol.Queue), id)).IsNull();
        }
        await EqualAsync(state.Owner.GetOutboxStatus(AtomicTriadStagedProtocol.Root, state.Partition),
            new OutboxStatus(new(0L, 1L, 0L, 0L), []));
    }

    internal static async Task CompleteAsync(AtomicTriadStagedFixture state)
    {
        await EqualAsync(state.Owner.GetDocument(AtomicTriadStagedProtocol.Root,
            new(state.Partition, AtomicTriadStagedProtocol.Collection, AtomicTriadStagedProtocol.Entity)),
            new DocumentResult(new(state.Partition, AtomicTriadStagedProtocol.Collection, AtomicTriadStagedProtocol.Entity),
                2L, AtomicTriadStagedProtocol.SecondJson, false, []));
        var stream = new StreamRef(state.Partition, AtomicTriadStagedProtocol.Streams, AtomicTriadStagedProtocol.Entity);
        var page = state.Owner.ReadStream(AtomicTriadStagedProtocol.Root, stream);
        await EqualAsync(page.Head, new StreamHead(2L, 1L, 1L));
        await Assert.That(page.HasMore).IsFalse();
        await EqualAsync(page.Events.ToArray(), new EventRecord[]
        {
            new(stream, 1L, 1L, new(AtomicTriadStagedProtocol.FirstEvent, AtomicTriadStagedProtocol.Created, AtomicTriadStagedProtocol.FirstJson, AtomicTriadStagedProtocol.Headers), state.CommittedAt),
            new(stream, 2L, 2L, new(AtomicTriadStagedProtocol.SecondEvent, AtomicTriadStagedProtocol.Replaced, AtomicTriadStagedProtocol.SecondJson, AtomicTriadStagedProtocol.Headers), state.CommittedAt)
        });
        await MessageAsync(state, AtomicTriadStagedProtocol.FirstMessage, AtomicTriadStagedProtocol.FirstJson, 1L);
        await MessageAsync(state, AtomicTriadStagedProtocol.SecondMessage, AtomicTriadStagedProtocol.SecondJson, 2L);
        await AtomicTriadStagedOutbox.CompleteAsync(state);
        await AtomicTriadStagedIndexes.CompleteAsync(state);
    }

    private static async Task MessageAsync(AtomicTriadStagedFixture state, string id, string json, long sequence)
        => await EqualAsync(state.Owner.InspectMessage(AtomicTriadStagedProtocol.Root,
            new(state.Partition, AtomicTriadStagedProtocol.Queue), id),
            new MessageInspection(new(id, MessageState.Ready, 0, 1L, sequence, null, null), json, AtomicTriadStagedProtocol.Headers));

    internal static async Task RequireProducedAsync(AtomicTriadStagedFixture state)
    {
        var failures = new List<Exception>();
        if (!state.HealthyCommitted)
        {
            failures.Add(new InvalidOperationException(AtomicTriadStagedProtocol.Missing));
            await ServerFailureObserver.ObserveAsync(() => EmptyAsync(state), failures);
        }
        else
        {
            await ServerFailureObserver.ObserveAsync(() => CompleteAsync(state), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task StableAsync(AtomicTriadStagedFixture state, Func<Task> action)
    {
        var bytes = QueueWholeFlowStorage.Bytes(state.Store);
        var position = state.Store.Position;
        var identity = OrleansSerialization.Serialize(state.Store.Identity);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(action, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(state.Store.Position).IsEqualTo(position);
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(QueueWholeFlowStorage.Bytes(state.Store)).IsEquivalentTo(bytes, TUnit.Assertions.Enums.CollectionOrdering.Matching), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await Assert.That(OrleansSerialization.Serialize(state.Store.Identity).AsSpan().SequenceEqual(identity)).IsTrue(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
