using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedTextWitnessValidation
{
    private const string CutChanged = "The distributed search statistics cut or authority changed.";

    internal static void Require(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, string digest, DistributedTextWitnessV1 witness,
        PartitionRef partition, global::KeyLoad.Core.Features.ResourceExecution.Execution.ReadExecutionBudgetReadGrant grant, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(witness);
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        var placement = DatabaseEngine.ReadAtomicPartitionPlacementForAuthorizedQuery(view, partition, grant);
        PartitionQueryPlacementValidation.Validate(placement, partition, witness.Owner);
        var identity = database.Store.Identity;
        if (witness.Partition != partition || witness.NodeId != identity.NodeId
            || witness.Incarnation != identity.Incarnation || witness.ReadGeneration != identity.ReadGeneration || witness.CutPosition != database.Store.Position
            || witness.PolicyEpoch != principal.PolicyEpoch || witness.SchemaVersion != resource.SchemaVersion
            || !string.Equals(witness.ResourcePolicyDigest, digest, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, CutChanged); }
        budget.Check();
    }
}
