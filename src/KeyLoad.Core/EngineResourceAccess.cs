using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes EngineResourceAccess operations against the caller's database context.</summary>
public static class EngineResourceAccess
{
    internal const int DatabaseEngineNoRetainedBytes = 0;
    internal const string DatabaseEngineResourceIsNotConfiguredDetail = "The resource is not configured.";
    internal const string DatabaseEngineResourceBelongsToADifferentTransactionDomainDetail = "The resource belongs to a different transaction domain.";
    internal const string DatabaseEngineResourceHasADifferentKindDetail = "The resource has a different kind.";
    internal const int DatabaseEngineMinimumPositiveCount = 1;
    internal const int NoPersistedCopies = 0;
    /// <summary>Reads a configured resource and checks its transaction domain and optional kind.</summary>
    /// <param name = "view">Current gated storage view.</param>
    /// <param name = "partition">Atomic partition containing the operation.</param>
    /// <param name = "name">Resource identifier.</param>
    /// <param name = "kind">Optional required resource kind.</param>
    /// <returns>The persisted resource definition.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static ResourceDefinition Resource(this DatabaseEngine engine, IKeyValueView view, PartitionRef partition, string name, ResourceKind? kind = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(partition);
        return ResourceExecutionQueriesResourceDefinitionBinding.BindResourceDefinition(view, partition, name, kind);
    }

    /// <summary>Creates a commit token from the local storage identity in the command's existing execution view.</summary>
    /// <param name = "view">The command's already-owned transaction or committed read view.</param>
    /// <param name = "partition">Complete atomic partition identity.</param>
    /// <param name = "position">Applied commit position.</param>
    /// <returns>The token bound to the resolved storage incarnation.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    internal static CommitToken Token(this DatabaseEngine engine, IKeyValueView view, PartitionRef partition, long position)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(partition);
        var owner = engine.ReadPlacementWitness(view, partition);
        return new(owner.Incarnation, partition.AtomicPartitionId, position);
    }

    internal static void ValidateCommitToken(this DatabaseEngine engine, IKeyValueView view, PartitionRef partition, CommitToken token, ErrorCode failureCode, string safeDetail)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(token);
        var owner = engine.ReadPlacementWitness(view, partition);
        if (token.Incarnation != owner.Incarnation || token.AtomicPartitionId != partition.AtomicPartitionId || token.Position < DatabaseEngineMinimumPositiveCount)
        {
            throw Errors.Fail(failureCode, safeDetail);
        }
    }

    /// <summary>Validates every component of an atomic partition identity.</summary>
    /// <param name = "partition">Complete identity to validate.</param>
    public static void ValidatePartition(PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        JsonData.Identifier(partition.TenantId);
        JsonData.Identifier(partition.DatabaseId);
        JsonData.Identifier(partition.TransactionDomainId);
        JsonData.Identifier(partition.PartitionKey);
    }
}
