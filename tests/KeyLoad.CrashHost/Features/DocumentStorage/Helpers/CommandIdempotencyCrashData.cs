using System.Text;
using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.CrashHost.Features.DocumentStorage;

internal static class CommandIdempotencyCrashData
{
    private const string CollectionCommand = "ab44c50a-6ba4-45c2-bc8b-3403a73b6901";
    private const string StreamCommand = "4ff09e34-5452-4c46-a9d1-b4626fcc15f8";
    private const string QueueCommand = "a69f11b9-b3a6-4d3c-b13b-6cb9428872a4";

    internal static void ConfigureResources(DatabaseEngine database)
    {
        Configure(database, CollectionCommand, CommandIdempotencyCrashContract.Collection, ResourceKind.Collection);
        Configure(database, StreamCommand, CommandIdempotencyCrashContract.StreamSet, ResourceKind.StreamSet);
        Configure(database, QueueCommand, CommandIdempotencyCrashContract.Queue, ResourceKind.WorkQueue);
    }

    internal static ReplicatedOperation CreateOperation()
    {
        var command = new CommandRequest(CommandIdempotencyCrashContract.CommandId, CommandIdempotencyCrashContract.Partition,
        [
            new PutDocument(CommandIdempotencyCrashContract.Collection, CommandIdempotencyCrashContract.DocumentId, CommandIdempotencyCrashContract.DocumentJson, 0),
            new AppendEvents(CommandIdempotencyCrashContract.StreamSet, CommandIdempotencyCrashContract.StreamId,
                [new(CommandIdempotencyCrashContract.EventId, CommandIdempotencyCrashContract.EventType, CommandIdempotencyCrashContract.EventPayloadJson)], ExpectedStreamRevision.NoStream),
            new EnqueueMessage(CommandIdempotencyCrashContract.Queue, CommandIdempotencyCrashContract.MessageId, CommandIdempotencyCrashContract.QueuePayloadJson)
        ]);
        return CrashDatabase.Operation(OperationKind.Batch, command, CommandIdempotencyCrashContract.CommandId);
    }

    internal static ReplicatedOperation CreateChangedOperation(ReplicatedOperation operation)
    {
        var command = JsonDefaults.Deserialize<CommandRequest>(Encoding.UTF8.GetBytes(operation.PayloadJson));
        var changed = command.Mutations.SetItem(0,
            new PutDocument(CommandIdempotencyCrashContract.Collection, CommandIdempotencyCrashContract.DocumentId, CommandIdempotencyCrashContract.ChangedDocumentJson, 0));
        var payload = JsonSerializer.Serialize(command with { Mutations = changed }, JsonDefaults.Options);
        return operation with { PayloadJson = payload };
    }

    internal static CommitReceipt CreateFollowUp(DatabaseEngine database)
    {
        var id = CommandIdempotencyCrashContract.FollowUpCommandId;
        var request = new CommandRequest(id, CommandIdempotencyCrashContract.Partition,
            [new PutDocument(CommandIdempotencyCrashContract.Collection, CommandIdempotencyCrashContract.FollowUpDocumentId, CommandIdempotencyCrashContract.FollowUpJson, 0)]);
        return CrashDatabase.Submit(database, OperationKind.Batch, request, id).Get<CommitReceipt>();
    }

    internal static async Task SaveEvidenceAsync<T>(string directory, string fileName, T value)
    {
        var bytes = NativeSerialization.Serialize(value);
        if (bytes.Length is <= 0 or > CommandIdempotencyCrashContract.EvidenceMaximumBytes)
        {
            throw new InvalidOperationException("Command restart evidence exceeded its fixed bound.");
        }
        await File.WriteAllBytesAsync(Path.Combine(directory, fileName), bytes);
    }

    internal static async Task<T> ReadEvidenceAsync<T>(string directory, string fileName, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path) || new FileInfo(path).Length is <= 0 or > CommandIdempotencyCrashContract.EvidenceMaximumBytes)
        {
            throw new InvalidOperationException("Command restart evidence was absent or exceeded its fixed bound.");
        }
        return NativeSerialization.Deserialize<T>(await File.ReadAllBytesAsync(path, cancellationToken));
    }

    private static void Configure(DatabaseEngine database, string commandId, string name, ResourceKind kind)
    {
        var request = new ConfigureResourceRequest(CommandIdempotencyCrashContract.Partition.TenantId, CommandIdempotencyCrashContract.Partition.DatabaseId,
            new(name, kind, CommandIdempotencyCrashContract.Partition.TransactionDomainId));
        _ = CrashDatabase.Submit(database, OperationKind.ConfigureResource, request, Guid.Parse(commandId))
            .Get<ResourceDefinition>();
    }
}
