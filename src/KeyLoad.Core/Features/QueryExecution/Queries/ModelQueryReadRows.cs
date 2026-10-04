using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.QueryExecution;

/// <summary>Dispatches read-only model source visits and validates their JSON records.</summary>
internal static class ModelQueryReadRows
{
    internal static void Visit(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, ResourceDefinition resource, ModelQuerySource source,
        ReadExecutionBudget budget, bool explain, Action<DocumentRecord> accept)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(accept);

        switch (source.Kind)
        {
            case ModelQuerySourceKind.Events:
                ModelEventQueryRows.Visit(database, view, partition, resource, source, budget, explain, accept);
                break;
            case ModelQuerySourceKind.QueueMessages:
                ModelQueueQueryRows.Visit(database, view, principal, partition, resource, source, budget, explain, accept);
                break;
            default:
                throw Errors.Fail(ErrorCode.UnsupportedCapability, "The model query source is unsupported.");
        }
    }

    internal static JsonDocument ParseObject(string json, string error)
    {
        try
        {
            var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                throw Errors.Fail(ErrorCode.Corruption, error);
            }
            return document;
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Corruption, error);
        }
    }

    internal static bool IsIdentifier(string? value)
    {
        try
        {
            JsonData.Identifier(value!);
            return true;
        }
        catch (KeyLoadException)
        {
            return false;
        }
    }
}
