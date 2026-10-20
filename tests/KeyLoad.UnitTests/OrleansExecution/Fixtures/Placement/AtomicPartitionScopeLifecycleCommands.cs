using System.Text.Json;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal static class AtomicPartitionScopeLifecycleCommands
{
    internal const string Documents = "documents";
    internal const string Events = "events";
    internal const string Queue = "queue";
    internal const string ForeignQueue = "foreign";
    internal const string FirstJson = "{\"number\":1}";
    internal const string HealthyJson = "{\"number\":2}";
    internal const string DomainDetail = "The resource belongs to a different transaction domain.";
    internal const string UniqueDetail = "A partition-scoped unique index value is already present.";
    internal static CommandRequest Write(Guid id, PartitionRef partition, bool healthy)
    {
        var item = healthy ? "healthy" : "item";
        var json = healthy ? HealthyJson : FirstJson;
        return new(id, partition, [new PutDocument(Documents, item, json, 0),
            new AppendEvents(Events, item, [new(item, "Created", json)], ExpectedStreamRevision.NoStream),
            new EnqueueMessage(Queue, item, json)]);
    }
    internal static CommandRequest Domain(PartitionRef partition)
        => new(Guid.NewGuid(), partition, [new PutDocument(Documents, "refused", HealthyJson, 0),
            new AppendEvents(Events, "refused", [new("refused", "Created", HealthyJson)], ExpectedStreamRevision.NoStream),
            new EnqueueMessage(ForeignQueue, "refused", HealthyJson)]);
    internal static CommandRequest Unique(PartitionRef partition)
        => new(Guid.NewGuid(), partition, [new AppendEvents(Events, "refused", [new("refused", "Created", HealthyJson)], ExpectedStreamRevision.NoStream),
            new EnqueueMessage(Queue, "refused", HealthyJson), new PutDocument(Documents, "refused", FirstJson, 0)]);
    internal static Result<object> Apply(DatabaseEngine database, CommandRequest request, CancellationToken token)
        => database.ApplyEmbedded(database.CreateCommand(OperationKind.Batch, request.CommandId, "root", default, JsonSerializer.Serialize(request, JsonDefaults.Options)), token);
    internal static void Configure(TestDatabase fixture)
    {
        fixture.Configure(Documents, ResourceKind.Collection, indexes: [new("number", ["/number"], true)]);
        fixture.Configure(Events, ResourceKind.StreamSet);
        fixture.Configure(Queue, ResourceKind.WorkQueue);
        fixture.Configure(ForeignQueue, ResourceKind.WorkQueue, domain: "foreign-domain");
    }
}
