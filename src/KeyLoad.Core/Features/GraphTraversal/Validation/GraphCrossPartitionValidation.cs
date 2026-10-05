using System.Text.Json;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core.Features.GraphTraversal.Validation;

internal static class GraphCrossPartitionValidation
{
    private const string InvalidRequest = "The graph cross-partition request is invalid.";
    private const string InvalidStoredRecord = "A committed graph cross-partition record is malformed.";

    internal static void ValidateLocator(PartitionRef source, string graph, string edgeId,
        EntityRef destination, long expectedRevision)
    {
        if (source is null || graph is null || edgeId is null || destination is null
            || destination.Partition is null || expectedRevision <= 0
            || HasNullPartitionPart(source) || HasNullPartitionPart(destination.Partition)
            || destination.Collection is null || destination.Id is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        try
        {
            DatabaseEngine.ValidatePartition(source);
            DatabaseEngine.ValidatePartition(destination.Partition);
            JsonData.Identifier(graph);
            JsonData.Identifier(edgeId);
            JsonData.Identifier(destination.Collection);
            JsonData.Identifier(destination.Id);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Validation)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
    }

    internal static void ValidateIncoming(ReadIncomingGraphEdgesRequestV1 request, DatabaseLimits limits,
        ReadExecutionBudget budget)
    {
        if (request is null || request.Version != GraphCrossPartitionProtocol.CurrentVersion
            || request.Target is null || request.Target.Partition is null || request.Graph is null
            || request.Limit < 1 || request.Limit > limits.MaxResults
            || HasNullPartitionPart(request.Target.Partition) || request.Target.Collection is null
            || request.Target.Id is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        try
        {
            budget.Check();
            DatabaseEngine.ValidatePartition(request.Target.Partition);
            JsonData.Identifier(request.Graph);
            JsonData.Identifier(request.Target.Collection);
            JsonData.Identifier(request.Target.Id);
            budget.Check();
            using var counter = new ResultByteCounterStream(limits.MaxQueryBytes, budget.Check);
            JsonSerializer.Serialize(counter, request, JsonDefaults.Options);
            budget.Check();
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Validation)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        catch (Exception error) when (error is JsonException or NotSupportedException
            or InvalidOperationException or ArgumentException)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
    }

    internal static void ValidateOwnerVersion(GraphEdgeOwnerVersionV1? record)
    {
        if (record is null || record.Version != GraphCrossPartitionProtocol.CurrentVersion || record.Revision <= 0)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidStoredRecord);
        }
    }

    internal static void ValidateIntent(GraphCrossPartitionDeliveryIntentV1? intent,
        PartitionRef source, string graph, string edgeId, EntityRef destination, DatabaseLimits limits)
    {
        if (HasInvalidIntentIdentity(intent, source, graph, edgeId, destination)
            || HasInvalidIntentPolicy(intent!)
            || HasInvalidIntentEdge(intent!, source, edgeId, destination))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidStoredRecord);
        }
        ValidateStoredLocator(intent!.SourcePartition, intent.Graph, intent.EdgeId,
            intent.Destination, intent.Revision);
        ValidateStoredIdentifier(intent.OriginalPrincipalId);
        ValidateEdge(intent.Edge, limits);
    }

    private static bool HasInvalidIntentIdentity(GraphCrossPartitionDeliveryIntentV1? intent,
        PartitionRef source, string graph, string edgeId, EntityRef destination)
        => intent is null || intent.Version != GraphCrossPartitionProtocol.CurrentVersion
            || intent.SourcePartition != source || intent.Graph != graph || intent.EdgeId != edgeId
            || intent.SourcePartition is null || HasNullPartitionPart(intent.SourcePartition)
            || intent.Destination != destination || intent.Destination.Partition is null
            || HasNullPartitionPart(intent.Destination.Partition) || intent.Destination.Collection is null
            || intent.Destination.Id is null || intent.Revision <= 0 || intent.Edge is null
            || intent.SourcePartition == intent.Destination.Partition;

    private static bool HasInvalidIntentPolicy(GraphCrossPartitionDeliveryIntentV1 intent)
        => string.IsNullOrWhiteSpace(intent.OriginalPrincipalId) || intent.OriginalPolicyEpoch < 0
            || !ValidFingerprint(intent.Fingerprint);

    private static bool HasInvalidIntentEdge(GraphCrossPartitionDeliveryIntentV1 intent,
        PartitionRef source, string edgeId, EntityRef destination)
        => intent.Edge.Id != edgeId
            || intent.Edge.From?.Partition != source || intent.Edge.To != destination
            || intent.Edge.Revision != intent.Revision;

    internal static void ValidateReceiver(GraphCrossPartitionReceiverStateV1? state,
        PartitionRef source, string graph, string edgeId, EntityRef destination)
    {
        if (state is null || state.Version != GraphCrossPartitionProtocol.CurrentVersion
            || state.SourcePartition != source || state.Graph != graph || state.EdgeId != edgeId
            || state.SourcePartition is null || HasNullPartitionPart(state.SourcePartition)
            || state.Destination != destination || state.Destination.Partition is null
            || HasNullPartitionPart(state.Destination.Partition) || state.Destination.Collection is null
            || state.Destination.Id is null || state.Revision <= 0 || !ValidFingerprint(state.Fingerprint)
            || state.SourcePartition == state.Destination.Partition)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidStoredRecord);
        }
        ValidateStoredLocator(state.SourcePartition, state.Graph, state.EdgeId,
            state.Destination, state.Revision);
    }

    internal static void ValidateCapacity(GraphCrossPartitionCapacityV1? capacity,
        GraphCrossPartitionCapacityDirection direction)
    {
        if (capacity is null || capacity.Version != GraphCrossPartitionProtocol.CurrentVersion
            || !Enum.IsDefined(capacity.Direction) || capacity.Direction != direction
            || capacity.RecordCount < 0 || capacity.EncodedBytes < 0)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidStoredRecord);
        }
    }

    internal static bool HasNullPartitionPart(PartitionRef partition)
        => partition.TenantId is null || partition.DatabaseId is null
           || partition.TransactionDomainId is null || partition.PartitionKey is null;

    private static bool ValidFingerprint(string? value)
        => value is { Length: 64 } && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static void ValidateEdge(EdgeRecord edge, DatabaseLimits limits)
    {
        if (edge.From is null || edge.To is null || edge.From.Partition is null || edge.To.Partition is null
            || edge.Id is null || edge.Label is null || edge.AttributesJson is null || edge.Revision <= 0)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidStoredRecord);
        }
        try
        {
            DatabaseEngine.ValidatePartition(edge.From.Partition);
            DatabaseEngine.ValidatePartition(edge.To.Partition);
            JsonData.Identifier(edge.Id);
            JsonData.Identifier(edge.Label);
            JsonData.Identifier(edge.From.Collection);
            JsonData.Identifier(edge.From.Id);
            JsonData.Identifier(edge.To.Collection);
            JsonData.Identifier(edge.To.Id);
            if (JsonData.Validate(edge.AttributesJson, limits) != edge.AttributesJson)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidStoredRecord);
            }
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Validation)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidStoredRecord);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.ResourceExhausted)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidStoredRecord);
        }
    }

    private static void ValidateStoredLocator(PartitionRef source, string graph, string edgeId,
        EntityRef destination, long revision)
    {
        try
        {
            ValidateLocator(source, graph, edgeId, destination, revision);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Validation)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidStoredRecord);
        }
    }

    private static void ValidateStoredIdentifier(string value)
    {
        try
        {
            JsonData.Identifier(value);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Validation)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidStoredRecord);
        }
    }
}
