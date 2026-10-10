using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchStatisticsEpoch
{
    private const int VersionOne = 1;
    private const int FirstElement = 0;
    private const int PreviousElement = 1;
    private const int HexCharactersPerByte = 2;
    private const string InvalidScope = "The distributed search statistics scope is incomplete or incomparable.";

    internal static string Create(string requestDigest, ImmutableArray<DistributedTextWitnessV1> witnesses,
        IOptions<QueryExecutionOptions> options, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        if (requestDigest is null || requestDigest.Length != SHA256.HashSizeInBytes * HexCharactersPerByte
            || witnesses.IsDefaultOrEmpty || witnesses.Length > options.Value.MaximumPartitions)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidScope); }
        for (var index = FirstElement; index < witnesses.Length; index++)
        {
            budget.Check();
            var witness = witnesses[index];
            if (witness is null || witness.Statistics is null || witness.NodeId == Guid.Empty || witness.Incarnation == Guid.Empty
                || witness.ReadGeneration < FirstElement || witness.CutPosition < FirstElement
                || index > FirstElement && PartitionQueryOrder.ComparePartition(
                    witnesses[index - PreviousElement].Partition, witness.Partition) >= FirstElement)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidScope); }
            DistributedTextStatisticsValidation.Require(witness.Statistics, witness.Statistics.Terms, budget);
        }
        var input = new DistributedSearchEpochInputV1(VersionOne, requestDigest, witnesses);
        var admission = new GlobalBranchByteAdmission(budget.MaximumResultBytes, budget);
        admission.Accept(NativeSerialization.Measure(input));
        var bytes = NativeSerialization.Serialize(input);
        budget.Check();
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
