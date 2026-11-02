using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.QueryExecution;

internal static class DistributedTextWitnessValidation
{
    private const string CutChanged = "The distributed search statistics cut or authority changed.";

    internal static void Require(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, string digest, DistributedTextWitness witness,
        PartitionRef partition, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(database) /* KLD0041: BCL null validation accepts the database DatabaseEngine reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(view) /* KLD0041: BCL null validation accepts the view IKeyValueView reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(principal) /* KLD0041: BCL null validation accepts the principal PrincipalRecord reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(resource) /* KLD0041: BCL null validation accepts the resource ResourceDefinition reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(witness) /* KLD0041: BCL null validation accepts the witness DistributedTextWitness reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: BCL null validation accepts the budget ReadExecutionBudget reference through its object parameter. */;
        budget.Check();
        var placement = database.ReadAtomicPartitionPlacementForAuthorizedQuery(view, partition);
        PartitionQueryPlacementValidation.Validate(placement, partition, witness.Owner);
        var identity = database.Store.Identity;
        if (witness.Partition != partition || witness.NodeId != identity.NodeId
            || witness.Incarnation != identity.Incarnation || witness.ReadGeneration != identity.ReadGeneration || witness.CutPosition != database.Store.Position
            || witness.PolicyEpoch != principal.PolicyEpoch || witness.SchemaVersion != resource.SchemaVersion
            || !string.Equals(witness.ResourcePolicyDigest, digest, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, CutChanged);
        }
        budget.Check();
    }
}
