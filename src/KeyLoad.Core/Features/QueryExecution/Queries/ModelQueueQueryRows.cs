using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.QueryExecution;

/// <summary>Reads and validates one existing queue lane without delivery or lease authority.</summary>
internal static class ModelQueueQueryRows
{
    private const string MetadataSpace = "message-meta";
    private const string BodySpace = "message-body";
    private const string QueueCorrupt = "The retained queue source is inconsistent.";
    private const string ScanExceeded = "The model query scan exceeds its configured candidate budget.";

    internal static void Visit(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, ResourceDefinition resource, ModelQuerySource source, ReadExecutionBudget budget,
        bool explain, Action<DocumentRecord> accept)
    {
        if (source.Generation != 1 || source.Item != resource.Name)
        {
            throw Errors.Fail(ErrorCode.Validation, "The queue model source does not match its configured resource.");
        }
        if (explain)
        {
            return;
        }
        VisitMetadata(database, view, principal, partition, resource, budget, accept);
    }

    private static void VisitMetadata(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, ResourceDefinition resource, ReadExecutionBudget budget,
        Action<DocumentRecord> accept)
    {
        var prefix = KeySpace.Partition(MetadataSpace, partition, resource.Name);
        var scan = budget.VisitRange(view, prefix, database.Limits.MaxScanRecords, (key, bytes) =>
        {
            budget.Check();
            var metadata = Deserialize(bytes);
            ValidateMetadata(partition, resource, key, metadata);
            Accept(database, view, principal, partition, resource, metadata, budget, accept);
            return true;
        });
        if (scan.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ScanExceeded);
        }
    }

    private static MessageMetadata Deserialize(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return NativeSerialization.Deserialize<MessageMetadata>(bytes);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException
                                           or InvalidOperationException)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueCorrupt);
        }
    }

    private static void ValidateMetadata(PartitionRef partition, ResourceDefinition resource,
        ReadOnlySpan<byte> key, MessageMetadata metadata)
    {
        if (metadata is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueCorrupt);
        }
        if (!ModelQueryReadRows.IsIdentifier(metadata.Id))
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueCorrupt);
        }
        var expected = KeySpace.Partition(MetadataSpace, partition, resource.Name, metadata.Id);
        if (!Enum.IsDefined(metadata.State) || metadata.StateVersion < 1 || metadata.Attempts < 0
            || !key.SequenceEqual(expected))
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueCorrupt);
        }
    }

    private static void Accept(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, ResourceDefinition resource, MessageMetadata metadata, ReadExecutionBudget budget,
        Action<DocumentRecord> accept)
    {
        if (metadata.State == MessageState.DeadLettered)
        {
            database.Authorization.Require(principal, partition, resource.Name, Capability.DeadLettersRead);
        }
        var body = budget.ReadRecord<MessageBody>(view,
            KeySpace.Partition(BodySpace, partition, resource.Name, metadata.Id));
        ValidateBody(metadata, body);
        var json = CreateRowJson(metadata, body);
        accept(new(new(partition, resource.Name, metadata.Id), metadata.StateVersion, json,
            new RowAccess(), DateTimeOffset.UnixEpoch));
    }

    private static void ValidateBody(MessageMetadata metadata, MessageBody? body)
    {
        if (body is null && metadata.State is not (MessageState.Acked or MessageState.DeadLettered
                or MessageState.Cancelled or MessageState.Expired)
            || body is not null && body.Id != metadata.Id)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueCorrupt);
        }
    }

    private static string CreateRowJson(MessageMetadata metadata, MessageBody? body)
    {
        using var payload = body is null ? null : ModelQueryReadRows.ParseObject(body.PayloadJson, QueueCorrupt);
        using var headers = body is null ? null : ModelQueryReadRows.ParseObject(body.HeadersJson, QueueCorrupt);
        return JsonSerializer.Serialize(new
        {
            id = metadata.Id,
            state = metadata.State.ToString(),
            attempts = metadata.Attempts,
            stateVersion = metadata.StateVersion,
            notBefore = metadata.NotBefore,
            expiresAt = metadata.ExpiresAt,
            payload = payload?.RootElement ?? (JsonElement?)null,
            headers = headers?.RootElement ?? (JsonElement?)null
        }, JsonDefaults.Options);
    }
}
