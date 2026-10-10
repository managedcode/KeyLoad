using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchLeafResultValidation
{
    private const long NoBytes = 0;
    private const int NoRecords = 0;
    private const string InvalidResult = "The distributed search receiving result is inconsistent with its original phase.";

    internal static void Require(DistributedSearchOwnedLeafV1 request, DistributedSearchLeafResultV1 result,
        ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        if (result.Phase != request.Phase || result.Witness is null
            || result.Witness.Owner is null || result.Witness.Statistics is null
            || result.Witness.Partition != request.Search.Partition
            || !DistributedSearchWitnessEquality.SameOwner(request.Owner, result.Witness.Owner)
            || result.Witness.NodeId == Guid.Empty || result.Witness.Incarnation != request.Owner.Incarnation
            || result.ReadBytes < NoBytes || result.ReadBytes > request.MaxReadBytes
            || result.ExaminedRecords < NoRecords || result.ExaminedRecords > request.MaxExaminedRecords
            || !ValidSlots(request, result)
            || request.Witness is { } original && !DistributedSearchWitnessEquality.Same(original, result.Witness))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidResult); }
        DistributedTextStatisticsValidation.Require(result.Witness.Statistics, result.Witness.Statistics.Terms, budget);
        DistributedSearchReplyShapeValidation.Require(request, result, budget);
        budget.CheckResult(result);
    }

    private static bool ValidSlots(DistributedSearchOwnedLeafV1 request, DistributedSearchLeafResultV1 result)
        => request.Phase switch
        {
            DistributedSearchPhase.Statistics => result.Candidates is null && result.Projection is null
                && result.Witness.ReadBytes == result.ReadBytes && result.Witness.ExaminedRecords == result.ExaminedRecords,
            DistributedSearchPhase.Candidates => result.Candidates is { Witness: not null } candidate && result.Projection is null
                && DistributedSearchWitnessEquality.Same(result.Witness, candidate.Witness)
                && candidate.ReadBytes == result.ReadBytes && candidate.ExaminedRecords == result.ExaminedRecords,
            DistributedSearchPhase.Projection => result.Projection is { Witness: not null } projection && result.Candidates is null
                && DistributedSearchWitnessEquality.Same(result.Witness, projection.Witness)
                && projection.ReadBytes == result.ReadBytes && projection.ExaminedRecords == result.ExaminedRecords,
            DistributedSearchPhase.Revalidate => result.Candidates is null && result.Projection is null,
            _ => false
        };
}
