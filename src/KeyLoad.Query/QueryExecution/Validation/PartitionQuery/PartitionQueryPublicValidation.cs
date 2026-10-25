using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Query.QueryExecution;

internal static class PartitionQueryPublicValidation
{
    private const int FirstElementIndex = 0;
    private const int FirstOrdinal = 1;
    private const int AdjacentElementOffset = 1;

    private const int RequestVersion = 1;
    private const string InvalidRequest = "The partition query request is invalid.";
    private const string UnsupportedQuery = "The partition query contains unsupported query options.";

    internal static (AstQueryRequest Request, ImmutableArray<PartitionRef> Partitions) Prepare(
        PartitionQueryRequest? request, QueryExecutionOptions execution)
    {
        ValidateHeader(request);
        var partitions = OrderPartitions(request!.Partitions, execution.MaximumPartitions);
        ValidateOptions(request.Query);
        var ast = new AstQueryRequest(partitions[FirstElementIndex], request.Query, request.Parameters,
            request.AllowFullScan, AstVersion: request.AstVersion);
        var normalized = QueryValidation.Normalize(ast, execution);
        return (normalized, partitions);
    }

    private static void ValidateHeader(PartitionQueryRequest? request)
    {
        if (request is null || request.Query is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        if (request.Version != RequestVersion)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        if (request.AstVersion != SqlOperationProtocol.DefaultQueryDialectVersion)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, InvalidRequest);
        }
    }

    private static ImmutableArray<PartitionRef> OrderPartitions(ImmutableArray<PartitionRef> partitions, int maximumPartitions)
    {
        if (partitions.IsDefaultOrEmpty || partitions.Length > maximumPartitions)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }

        foreach (var partition in partitions)
        {
            if (partition is null)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
            }
            EngineResourceAccess.ValidatePartition(partition);
        }
        var ordered = partitions.OrderBy(item => item.TenantId, StringComparer.Ordinal)
            .ThenBy(item => item.DatabaseId, StringComparer.Ordinal)
            .ThenBy(item => item.TransactionDomainId, StringComparer.Ordinal)
            .ThenBy(item => item.PartitionKey, StringComparer.Ordinal).ToImmutableArray();
        for (var index = FirstOrdinal; index < ordered.Length; index++)
        {
            if (ordered[index] == ordered[index - AdjacentElementOffset])
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
            }
        }
        return ordered;
    }

    private static void ValidateOptions(SelectQuery query)
    {
        if (query.Explain || query.ModelSource is not null)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, UnsupportedQuery);
        }
    }
}
