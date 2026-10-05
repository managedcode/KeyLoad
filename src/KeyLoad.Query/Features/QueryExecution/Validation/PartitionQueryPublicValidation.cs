using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class PartitionQueryPublicValidation
{
    private const int RequestVersion = 1;
    private const int MaximumPartitions = 8;
    private const string InvalidRequest = "The partition query request is invalid.";
    private const string UnsupportedQuery = "The partition query contains unsupported query options.";
    private const string RequestBytesExceeded = "The partition query request exceeds its byte budget.";

    internal static (AstQueryRequest Request, ImmutableArray<PartitionRef> Partitions) Prepare(
        PartitionQueryRequestV1? request, DatabaseLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ValidateHeader(request);
        var partitions = OrderPartitions(request!.Partitions);
        ValidateOptions(request.Query);
        var ast = new AstQueryRequest(partitions[0], request.Query, request.Parameters,
            request.AllowFullScan, AstVersion: request.AstVersion);
        var normalized = QueryValidation.Normalize(ast, limits);
        var boundedRequest = request with
        {
            Partitions = partitions,
            Query = normalized.Query,
            Parameters = normalized.Parameters
        };
        ValidateRequestBytes(boundedRequest, limits);
        return (normalized, partitions);
    }

    private static void ValidateHeader(PartitionQueryRequestV1? request)
    {
        if (request is null || request.Query is null)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        if (request.Version != RequestVersion)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }
        if (request.AstVersion != RequestVersion)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, InvalidRequest);
        }
    }

    private static ImmutableArray<PartitionRef> OrderPartitions(ImmutableArray<PartitionRef> partitions)
    {
        if (partitions.IsDefaultOrEmpty || partitions.Length > MaximumPartitions)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }

        foreach (var partition in partitions)
        {
            if (partition is null)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
            }
            DatabaseEngine.ValidatePartition(partition);
        }
        var ordered = partitions.OrderBy(item => item.TenantId, StringComparer.Ordinal)
            .ThenBy(item => item.DatabaseId, StringComparer.Ordinal)
            .ThenBy(item => item.TransactionDomainId, StringComparer.Ordinal)
            .ThenBy(item => item.PartitionKey, StringComparer.Ordinal).ToImmutableArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index] == ordered[index - 1])
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

    private static void ValidateRequestBytes(PartitionQueryRequestV1 request, DatabaseLimits limits)
    {
        var length = JsonDefaults.Serialize(request).Length;
        if (length > limits.MaxQueryBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RequestBytesExceeded);
        }
    }
}
