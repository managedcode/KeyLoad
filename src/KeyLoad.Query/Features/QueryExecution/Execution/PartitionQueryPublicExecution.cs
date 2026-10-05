using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class PartitionQueryPublicExecution
{
    private const string InvalidOwner = "The server physical owner identity is invalid.";

    internal static PartitionQueryPageV1 Execute(QueryEngine engine, string principalId,
        PartitionQueryRequestV1 request, PhysicalShardRecord expectedOwner,
        TimeProvider? timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        ValidateOwner(expectedOwner);
        var owner = engine.PartitionQueryOwner;
        var budget = new ReadExecutionBudget(owner.Limits, timeProvider ?? owner.EvaluationClock,
            cancellationToken);
        budget.Check();
        using var admission = owner.AdmitQuery(cancellationToken);
        budget.Check();
        var result = engine.ExecutePartitionQuery(principalId, request, budget, expectedOwner);
        return PartitionQueryPublicMapper.MapPublic(result, owner.Limits, budget);
    }

    private static void ValidateOwner(PhysicalShardRecord? owner)
    {
        if (owner is null || owner.PhysicalShardId == Guid.Empty || owner.Incarnation == Guid.Empty
            || owner.VoterIds.IsDefaultOrEmpty || owner.PlacementEpoch < 1)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, InvalidOwner);
        }
    }
}
