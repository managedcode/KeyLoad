using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Query.QueryExecution;

internal static class PartitionQueryPublicExecution
{
    private const string InvalidOwner = "The server physical owner identity is invalid.";

    internal static PartitionQueryPage Execute(QueryEngine engine, string principalId,
        PartitionQueryRequest request, PhysicalShardRecord expectedOwner,
        TimeProvider? timeProvider, CancellationToken cancellationToken, IResourceLifetimeWorkBinding? binding = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        ValidateOwner(expectedOwner);
        var owner = engine.PartitionQueryOwner;
        var budget = new ReadExecutionBudget(timeProvider ?? owner.EvaluationClock,
            cancellationToken);
        if (binding is not null)
        {
            budget.AttachResourceLifetimeBinding(binding);
        }
        budget.Check();
        using var admission = owner.AdmitQuery(cancellationToken);
        budget.Check();
        var result = engine.ExecutePartitionQuery(principalId, request, budget, expectedOwner);
        return PartitionQueryPublicMapper.MapPublic(result, budget);
    }

    private static void ValidateOwner(PhysicalShardRecord? owner)
    {
        if (owner is null || owner.PhysicalShardId == Guid.Empty || owner.Incarnation == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, InvalidOwner);
        }
    }
}
