using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Query.Features.Search;

internal static class GraphSearchValidation
{
    private const string InvalidVersion = "The graph search version is unsupported.";
    private const string MissingOperator = "At least one graph operator is required.";
    private const string InvalidRequest = "The graph search request is invalid.";
    private const string InvalidWalk = "The graph walk specification is invalid.";
    private const int VersionOne = 1;

    internal static void Validate(GraphSearchRequest request, DatabaseLimits limits, ReadExecutionBudget budget,
        GraphExecutionOptions execution)
    {
        if (request.Version != VersionOne)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidVersion);
        }
        if (request.Scope is null && request.Retriever is null && request.Expansion is null)
        {
            throw Errors.Fail(ErrorCode.Validation, MissingOperator);
        }
        if (request.Search is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        SearchRequestValidation.Validate(request.Search, limits, request.Retriever is not null);
        DatabaseEngine.ValidatePartition(request.Search.Partition);
        JsonData.Identifier(request.Search.Collection);
        ValidateScope(request.Scope, request.Search.Partition, limits, budget, execution);
        ValidateRetriever(request.Retriever, request.Search.Partition, limits, budget, execution);
        ValidateExpansion(request.Expansion, execution);
    }

    private static void ValidateScope(GraphScope? scope, PartitionRef partition, DatabaseLimits limits,
        ReadExecutionBudget budget, GraphExecutionOptions execution)
    {
        if (scope is not null)
        {
            ValidateWalk(scope.Walk, partition, limits, budget, execution);
        }
    }

    private static void ValidateRetriever(GraphRetriever? retriever, PartitionRef partition, DatabaseLimits limits,
        ReadExecutionBudget budget, GraphExecutionOptions execution)
    {
        if (retriever is null)
        {
            return;
        }
        if (!double.IsFinite(retriever.Weight) || retriever.Weight < 0)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidWalk);
        }
        ValidateWalk(retriever.Walk, partition, limits, budget, execution);
    }

    private static void ValidateExpansion(GraphExpansion? expansion, GraphExecutionOptions execution)
    {
        if (expansion is null)
        {
            return;
        }
        if (expansion.MaxDepth < 0 || expansion.MaxDepth > execution.MaximumDepth
            || expansion.MaxVertices < 1 || expansion.MaxVertices > execution.MaximumVertices || expansion.MaxEdges < 1 || expansion.MaxEdges > execution.MaximumEdges)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidWalk);
        }
        JsonData.Identifier(expansion.Graph);
        ValidateLabels(expansion.Labels);
    }

    private static void ValidateWalk(GraphWalkSpec? walk, PartitionRef partition, DatabaseLimits limits,
        ReadExecutionBudget budget, GraphExecutionOptions execution)
    {
        if (walk is null || walk.Seeds.IsDefaultOrEmpty || walk.MaxDepth < 0 || walk.MaxDepth > execution.MaximumDepth
            || walk.MaxVertices < 1 || walk.MaxVertices > execution.MaximumVertices || walk.MaxEdges < 1 || walk.MaxEdges > execution.MaximumEdges
            || walk.Seeds.Length > walk.MaxVertices || walk.Seeds.Length > limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidWalk);
        }
        JsonData.Identifier(walk.Graph);
        foreach (var seed in walk.Seeds)
        {
            budget.Check();
            if (seed is null || seed.Partition != partition)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidWalk);
            }
            JsonData.Identifier(seed.Collection);
            JsonData.Identifier(seed.Id);
        }
        ValidateLabels(walk.Labels);
    }

    private static void ValidateLabels(ImmutableArray<string>? labels)
    {
        if (labels is not { } values)
        {
            return;
        }
        if (values.IsDefault)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidWalk);
        }
        foreach (var label in values)
        {
            JsonData.Identifier(label);
        }
    }
}
