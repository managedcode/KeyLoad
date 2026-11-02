using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Storage;

namespace KeyLoad.Core.QueryExecution;

/// <summary>Dispatches read-only model source visits and validates their JSON records.</summary>
internal static class ModelQueryReadRows
{
    internal static void Visit(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, ResourceDefinition resource, ModelQuerySource source,
        ReadExecutionBudget budget, bool explain, Action<DocumentRecord> accept)
    {
        const string VisitDetailText = "The model query source is unsupported.";

        ArgumentNullException.ThrowIfNull(database) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(view) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(principal) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(partition) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(resource) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(source) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(accept) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;

        switch (source.Kind)
        {
            case ModelQuerySourceKind.Events:
                ModelEventQueryRows.Visit(view, partition, resource, source, budget, explain, accept);
                break;
            case ModelQuerySourceKind.QueueMessages:
                ModelQueueQueryRows.Visit(database, view, principal, partition, resource, source, budget, explain, accept);
                break;
            default:
                throw Errors.Fail(ErrorCode.UnsupportedCapability, VisitDetailText);
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
