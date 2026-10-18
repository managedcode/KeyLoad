using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueLifecycleNativeRecorder(DatabaseEngine database, QueueLaneRef lane,
    Func<string, EnqueueMessage> literal)
{
    private const int MetadataReadBound = 16;
    private const int NextCommitPosition = 1;
    private const string Subject = "root";
    private const string MetadataFamily = "message-meta";
    private const string CounterFamily = "queue-counters";
    private const string IncompleteFixture = "The native lifecycle recorder fixture exceeds its fixed metadata bound.";
    internal DatabaseEngine Database { get; set; } = database;
    internal List<QueueLifecycleNativeSample> Samples { get; } = [];

    private readonly Dictionary<(string Subject, Guid Id), QueueLifecycleNativeExpectation> expectations = [];

    internal Result<object> Apply(Command<DatabaseCommand> operation)
    {
        Observe(Database, operation);
        var result = Database.Apply(operation);
        Record(operation, result);
        return result;
    }

    internal void Observe(DatabaseEngine owner, Command<DatabaseCommand> operation)
    {
        var identity = (operation.UserId!, operation.CommandId);
        if (expectations.ContainsKey(identity))
        {
            return;
        }
        var store = owner.Store;
        var placement = owner.ReadAtomicPartitionPlacement(Subject, lane.Partition);
        var token = new CommitToken(placement.Incarnation, lane.Partition.AtomicPartitionId,
            checked(store.Position + NextCommitPosition));
        var before = store.Read(view =>
        {
            var page = view.Scan(KeySpace.Partition(MetadataFamily, lane.Partition, lane.Queue), MetadataReadBound);
            if (page.HasMore)
            {
                throw new InvalidOperationException(IncompleteFixture);
            }
            return page.Records.Select(row => NativeSerialization.Deserialize<MessageMetadata>(row.Value.Span))
                .ToDictionary(row => row.Id, StringComparer.Ordinal);
        });
        var counters = store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(CounterFamily, lane.Partition, lane.Queue)));
        expectations.Add(identity, new(token, before, counters));
    }

    internal void Capture(IEnumerable<(Command<DatabaseCommand> Operation, Result<object> Result)> outcomes)
    {
        Samples.Clear();
        foreach (var outcome in outcomes)
        {
            Record(outcome.Operation, outcome.Result);
        }
    }

    private void Record(Command<DatabaseCommand> operation, Result<object> result)
    {
        var expected = expectations[(operation.UserId!, operation.CommandId)];
        Samples.Add(new(operation, result, expected.Token, expected.Before, expected.Counters));
    }

    internal async Task AssertAsync(CancellationToken token)
    {
        foreach (var sample in Samples)
        {
            token.ThrowIfCancellationRequested();
            await QueueLifecycleNativeReceiptAssertions.ResultAsync(Database, lane, literal, sample);
        }
    }
}
