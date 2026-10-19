using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class AtomicPartitionScopeLifecycleModels
{
    internal static async Task CurrentAsync(DatabaseEngine database, AtomicPartitionScopeLifecycleState state,
        int? acceptedScope, CancellationToken token)
    {
        for (var index = 0; index < 2; index++)
        {
            var partition = state.Partition(index);
            await ItemAsync(database, partition, "item", AtomicPartitionScopeLifecycleCommands.FirstJson, state.Present[index], 1, state, token);
            await ItemAsync(database, partition, "healthy", AtomicPartitionScopeLifecycleCommands.HealthyJson, state.Healthy[index], 2, state, token);
            await ItemAsync(database, partition, "refused", AtomicPartitionScopeLifecycleCommands.HealthyJson, false, 0, state, token);
            var outbox = database.GetOutboxStatus("root", partition);
            var count = (state.Present[index] ? 3 : 0) + (state.Healthy[index] ? 3 : 0);
            await Assert.That(outbox.Head.Tail).IsEqualTo(count);
            await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(1);
            await Assert.That(outbox.Head.StoredRecords).IsEqualTo(count);
            await Assert.That(outbox.Consumers).IsEmpty();
            if (count == 0)
            {
                await Assert.That(outbox.Head.StoredBytes).IsEqualTo(0);
            }
            else
            {
                await Assert.That(outbox.Head.StoredBytes).IsGreaterThan(0);
            }
            var serialized = JsonSerializer.Serialize(outbox, JsonDefaults.Options);
            if (state.Outbox[index] is null || acceptedScope == index)
            {
                state.Outbox[index] = serialized;
            }
            await Assert.That(serialized).IsEqualTo(state.Outbox[index]);
        }
        await Assert.That(database.InspectMessage("root", new(state.Foreign, AtomicPartitionScopeLifecycleCommands.ForeignQueue), "refused")).IsNull();
    }
    private static async Task ItemAsync(DatabaseEngine database, PartitionRef partition,
        string item, string json, bool present, long sequence, AtomicPartitionScopeLifecycleState state, CancellationToken token)
    {
        var reference = new EntityRef(partition, AtomicPartitionScopeLifecycleCommands.Documents, item);
        var document = database.GetDocument("root", reference, cancellationToken: token);
        var stream = new StreamRef(partition, AtomicPartitionScopeLifecycleCommands.Events, item);
        var page = database.ReadStream("root", stream, cancellationToken: token);
        var message = database.InspectMessage("root", new(partition, AtomicPartitionScopeLifecycleCommands.Queue), item);
        await Assert.That(page.Stream).IsEqualTo(stream);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.CutPosition).IsGreaterThan(0);
        if (!present)
        {
            await Assert.That(document).IsNull();
            await Assert.That(page.Events).IsEmpty();
            await Assert.That(page.Head).IsEqualTo(new StreamHead(0, 1, 1));
            await Assert.That(message).IsNull();
            return;
        }
        await EqualAsync(document, new DocumentResult(reference, 1, json, false, []));
        await Assert.That(page.Head).IsEqualTo(new StreamHead(1, 1, 1));
        var record = page.Events.Single();
        await Assert.That(record.RecordedAt).IsGreaterThan(DateTimeOffset.MinValue);
        var key = partition.AtomicPartitionId + ":" + item;
        if (!state.EventTimes.TryGetValue(key, out var expectedTime))
        {
            expectedTime = record.RecordedAt;
            state.EventTimes.Add(key, expectedTime);
        }
        await EqualAsync(record, new EventRecord(stream, 1, sequence, new(item, "Created", json), expectedTime));
        await EqualAsync(message, new MessageInspection(new(item, MessageState.Ready, 0, 1, sequence, null, null), json, "{}"));
    }
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
}
