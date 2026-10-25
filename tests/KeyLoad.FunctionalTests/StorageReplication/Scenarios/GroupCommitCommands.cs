using System.Text.Json;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.StorageReplication;

/// <summary>Creates original document commands for one database and reads their stored documents.</summary>
internal static class GroupCommitCommands
{
    internal const string Collection = "group-commit-documents";
    internal const string Root = "root";

    internal static Command<DatabaseCommand> Put(TestDatabase fixture, string id, string json, long? expectedRevision = null)
    {
        var commandId = Guid.NewGuid();
        var request = new CommandRequest(commandId, fixture.Partition, [new PutDocument(Collection, id, json, expectedRevision)]);
        return fixture.Database.CreateCommand(OperationKind.Batch, commandId, Root,
            fixture.Database.EvaluationClock.GetUtcNow(), JsonSerializer.Serialize(request, JsonDefaults.Options));
    }

    internal static DocumentResult? Read(TestDatabase fixture, string id)
        => fixture.Database.GetDocument(Root, new(fixture.Partition, Collection, id));

    internal static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;
}
