using KeyLoad.Core;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class CompetingInboxState(PartitionRef partition, DateTimeOffset time, bool equivalent)
{
    internal const string Queue = "competing-input";
    internal const string Output = "competing-output";
    internal const string Documents = "competing-documents";
    internal const string Streams = "competing-events";
    internal PartitionRef Partition { get; } = partition;
    internal QueueLaneRef Lane { get; } = new(partition, Queue);
    internal DateTimeOffset Time { get; } = time;
    internal bool Equivalent { get; } = equivalent;
    internal Command<DatabaseCommand>[] Competitors { get; set; } = [];
    internal Result<object>[] Results { get; set; } = [];
    internal int Winner
    {
        get; set;
    }
    internal CommitReceipt Receipt { get; set; } = null!;
    internal CommitReceipt Producer { get; set; } = null!;
    internal long EffectPosition
    {
        get; set;
    }
    internal string Json(int index) => Equivalent || index == 0 ? "{\"winner\":1}" : "{\"winner\":2}";
    internal Mutation[] Effects(int index) => [new PutDocument(Documents, "effect", Json(index), 0),
        new AppendEvents(Streams, "effect", [new("effect", "Completed", Json(index))], ExpectedStreamRevision.NoStream),
        new EnqueueMessage(Output, "effect", Json(index))];
    internal Command<DatabaseCommand> Operation<T>(DatabaseEngine database, OperationKind kind, T request, Guid id)
        => database.CreateCommand(kind, id, "root", Time,
            System.Text.Json.JsonSerializer.Serialize(request, JsonDefaults.Options));
}
