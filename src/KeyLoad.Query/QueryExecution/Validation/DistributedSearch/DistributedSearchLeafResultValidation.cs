using KeyLoad.Core;

namespace KeyLoad.Query.QueryExecution;

internal static class DistributedSearchLeafResultValidation
{
    private const string InvalidResult = "The distributed search receiving result is inconsistent with its original phase.";

    internal static void Require(DistributedSearchOwnedLeaf request, DistributedSearchLeafResult result,
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
            || result.Witness.NodeId == Guid.Empty || result.Witness.NodeId != request.Owner.PhysicalShardId
            || result.Witness.Incarnation != request.Owner.Incarnation
            || !ValidSlots(request, result)
            || request.Witness is { } original && !DistributedSearchWitnessEquality.Same(original, result.Witness))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidResult);
        }
        DistributedTextStatisticsValidation.Require(result.Witness.Statistics, result.Witness.Statistics.Terms, budget);
        DistributedSearchReplyShapeValidation.Require(request, result, budget);
    }

    private static bool ValidSlots(DistributedSearchOwnedLeaf request, DistributedSearchLeafResult result)
        => request.Phase switch
        {
            DistributedSearchPhase.Statistics => result.Candidates is null && result.Projection is null,
            DistributedSearchPhase.Candidates => result.Candidates is { Witness: not null } candidate && result.Projection is null
                && DistributedSearchWitnessEquality.Same(result.Witness, candidate.Witness),
            DistributedSearchPhase.Projection => result.Projection is { Witness: not null } projection && result.Candidates is null
                && DistributedSearchWitnessEquality.Same(result.Witness, projection.Witness),
            DistributedSearchPhase.Revalidate => result.Candidates is null && result.Projection is null,
            _ => false
        };
}
