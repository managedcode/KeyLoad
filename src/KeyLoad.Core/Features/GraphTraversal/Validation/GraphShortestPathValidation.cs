using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core.Features.GraphTraversal;

/// <summary>Validates shortest-path caller shapes and native graph records.</summary>
internal static class GraphShortestPathValidation
{
    private const int FirstElementIndex = 0;

    private const int ContractVersion = 1;
    private const int MinimumDepth = 0;
    private const int MinimumVertices = 1;
    private const int MinimumEdges = 1;
    private const string AdjacencySpace = "adjacency";
    private const string OutDirection = "out";
    private const string InvalidRequest = "The graph shortest-path request is invalid.";
    private const string InvalidBudget = "The graph shortest-path budget is invalid.";
    private const string ForeignEndpoint = "Graph path endpoints must belong to the request partition.";
    private const string InvalidAdjacency = "A graph adjacency record is inconsistent with its canonical edge.";

    internal static string[]? Validate(GraphShortestPathRequest request, DatabaseLimits limits,
        ReadExecutionBudget budget, GraphExecutionOptions execution)
    {
        ValidateShape(request);
        MeasureRequest(request, limits.MaxQueryBytes, budget);
        ValidateIdentifiers(request, budget);
        if (request.MaxDepth < MinimumDepth || request.MaxDepth > execution.MaximumDepth
            || request.MaxVertices < MinimumVertices || request.MaxVertices > execution.MaximumVertices
            || request.MaxEdges < MinimumEdges || request.MaxEdges > execution.MaximumEdges)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidBudget);
        }
        if (request.From.Partition != request.Partition || request.To.Partition != request.Partition)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, ForeignEndpoint);
        }
        return Labels(request.Labels, budget, execution.MaximumLabels);
    }

    private static void ValidateShape(GraphShortestPathRequest request)
    {
        if (request is null || request.Version != ContractVersion || request.Partition is null
            || request.Graph is null || request.From is null || request.To is null
            || request.From.Partition is null || request.To.Partition is null
            || HasNullComponents(request.Partition) || HasNullComponents(request.From.Partition)
            || HasNullComponents(request.To.Partition)
            || request.Labels.HasValue && request.Labels.Value.IsDefault)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
    }

    private static bool HasNullComponents(PartitionRef partition)
        => partition.TenantId is null || partition.DatabaseId is null
           || partition.TransactionDomainId is null || partition.PartitionKey is null;

    private static void MeasureRequest(GraphShortestPathRequest request, int maximumBytes,
        ReadExecutionBudget budget)
    {
        budget.Check();
        try
        {
            using var counter = new ResultByteCounterStream(maximumBytes, budget.Check);
            JsonSerializer.Serialize(counter, request, JsonDefaults.Options);
            budget.Check();
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException
            or InvalidOperationException or ArgumentException)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
    }

    internal static void ValidateAdjacency(ReadOnlySpan<byte> key, string edgeId, EdgeRecord? edge,
        EntityRef source, PartitionRef partition, string graph, ReadExecutionBudget budget)
    {
        ValidateStoredIdentifier(edgeId, budget);
        if (edge is null || edgeId != edge.Id || edge.From != source || edge.To is null
            || edge.To.Partition != partition || !key.SequenceEqual(KeySpace.Partition(
                AdjacencySpace, partition, graph, OutDirection, source.Collection, source.Id, edgeId)))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidAdjacency);
        }
        ValidateStoredIdentifiers(edge, budget);
    }

    private static void ValidateIdentifiers(GraphShortestPathRequest request, ReadExecutionBudget budget)
    {
        try
        {
            budget.Check();
            DatabaseEngine.ValidatePartition(request.Partition);
            budget.Check();
            JsonData.Identifier(request.Graph);
            ValidateEntity(request.From, budget);
            ValidateEntity(request.To, budget);
        }
        catch (KeyLoadException exception) when (exception.Code == ErrorCode.Validation)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
    }

    private static void ValidateEntity(EntityRef entity, ReadExecutionBudget budget)
    {
        if (entity.Partition is null || entity.Collection is null || entity.Id is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        budget.Check();
        DatabaseEngine.ValidatePartition(entity.Partition);
        budget.Check();
        JsonData.Identifier(entity.Collection);
        budget.Check();
        JsonData.Identifier(entity.Id);
    }

    private static string[]? Labels(ImmutableArray<string>? labels, ReadExecutionBudget budget, int maximumLabels)
    {
        if (labels is null || labels.Value.IsEmpty)
        {
            return labels is null ? null : [];
        }
        if (labels.Value.Length > maximumLabels)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        var values = new string[labels.Value.Length];
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var index = FirstElementIndex; index < labels.Value.Length; index++)
        {
            budget.Check();
            var label = labels.Value[index] ?? throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
            try
            {
                JsonData.Identifier(label);
            }
            catch (KeyLoadException exception) when (exception.Code == ErrorCode.Validation)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
            }
            if (!unique.Add(label))
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
            }
            values[index] = label;
        }
        budget.Check();
        return values;
    }

    private static void ValidateStoredIdentifiers(EdgeRecord edge, ReadExecutionBudget budget)
    {
        try
        {
            ValidateStoredIdentifier(edge.Id, budget);
            ValidateStoredIdentifier(edge.Label, budget);
            ValidateStoredEntity(edge.To, budget);
        }
        catch (KeyLoadException exception) when (exception.Code == ErrorCode.Validation)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidAdjacency);
        }
    }

    private static void ValidateStoredEntity(EntityRef entity, ReadExecutionBudget budget)
    {
        if (entity is null || entity.Partition is null || entity.Collection is null || entity.Id is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        budget.Check();
        DatabaseEngine.ValidatePartition(entity.Partition);
        budget.Check();
        JsonData.Identifier(entity.Collection);
        budget.Check();
        JsonData.Identifier(entity.Id);
    }

    private static void ValidateStoredIdentifier(string? value, ReadExecutionBudget budget)
    {
        try
        {
            budget.Check();
            JsonData.Identifier(value!);
        }
        catch (KeyLoadException exception) when (exception.Code == ErrorCode.Validation)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidAdjacency);
        }
        catch (ArgumentNullException)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidAdjacency);
        }
    }
}
