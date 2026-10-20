using System.Collections.Immutable;
using System.Text.Json;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class AtomicTriadOutboxFixture
{
    internal const string Root = "root";
    internal const string Collection = "triad-documents";
    internal const string Streams = "triad-events";
    internal const string Queue = "triad-jobs";
    internal const string Entity = "composed";
    internal const string Payload = "{\"number\":7}";
    internal const string Headers = "{\"kind\":\"triad\"}";
    internal const int Capacity = 3;
    internal const int SeedCount = 1;
    internal const long InitialRevision = 1L;
    internal const string QuotaDetail = "The committed outbox quota is exhausted; advance consumers and reclaim retained entries.";

    internal static void Configure(TestDatabase database)
    {
        database.Configure(Collection, ResourceKind.Collection, indexes: [new("number", ["/number"], true)]);
        database.Configure(Streams, ResourceKind.StreamSet);
        database.Configure(Queue, ResourceKind.WorkQueue);
    }

    internal static CommandRequest Command(PartitionRef partition, int rotation)
    {
        Mutation[] mutations = [new PutDocument(Collection, Entity, Payload, 0L),
            new AppendEvents(Streams, Entity, [new(Entity, "Created", Payload, Headers)], ExpectedStreamRevision.NoStream),
            new EnqueueMessage(Queue, Entity, Payload, Headers)];
        return new(Guid.NewGuid(), partition, [.. mutations.Skip(rotation), .. mutations.Take(rotation)]);
    }

    internal static Command<DatabaseCommand> Operation<T>(DatabaseEngine database, OperationKind kind, Guid id, T payload)
        => database.CreateCommand(kind, id, Root, default, JsonSerializer.Serialize(payload, JsonDefaults.Options));

    internal static Command<DatabaseCommand> Operation(DatabaseEngine database, CommandRequest command)
        => Operation(database, OperationKind.Batch, command.CommandId, command);

    internal static ImmutableArray<MutationReceipt> Receipts(CommandRequest command)
        => [.. command.Mutations.Select(mutation => mutation switch
        {
            PutDocument => new MutationReceipt("putDocument", Collection, Entity, InitialRevision),
            AppendEvents => new MutationReceipt("appendEvents", Streams, Entity, InitialRevision),
            EnqueueMessage => new MutationReceipt("enqueue", Queue, Entity, InitialRevision),
            _ => throw new InvalidOperationException("The original mixed fixture requires exactly three known mutations.")
        })];
}
