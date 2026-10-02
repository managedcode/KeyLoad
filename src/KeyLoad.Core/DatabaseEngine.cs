using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Applies authorized database commands and reads through one node-owned atomic store.</summary>
/// <param name="store">The externally owned node-local atomic store.</param>
/// <param name="authorization">Evaluator for persisted operation, row and field policy.</param>
/// <param name="limits">Optional bounded operation limits.</param>
/// <param name="timeProvider">Optional business clock; hosting runtime time is unaffected.</param>
public sealed partial class DatabaseEngine(IAtomicStore store, IAuthorizationPolicy authorization, DatabaseLimits? limits = null,
    TimeProvider? timeProvider = null)
{
    private TimeProvider Clock { get; } = timeProvider ?? TimeProvider.System;
    /// <summary>Gets the borrowed business clock for consistent persisted read authority.</summary>
    public TimeProvider EvaluationClock => Clock;
    /// <summary>Gets the externally owned atomic store used by this engine.</summary>
    public IAtomicStore Store { get; } = store;
    /// <summary>Gets the evaluator for persisted authorization policy.</summary>
    public IAuthorizationPolicy Authorization { get; } = authorization;
    /// <summary>Gets the configured operation resource limits.</summary>
    public DatabaseLimits Limits { get; } = limits ?? new();
    /// <summary>Gets or sets the acknowledgement durability reported in commit receipts.</summary>
    public DurabilityProfile Durability { get; set; } = store.Identity.Durability;
    /// <summary>Gets the canonical store's last applied replicated position.</summary>
    public long LastApplied => Store.Read(view => view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes ? JsonDefaults.Deserialize<long>(bytes) : 0);

    /// <summary>Reads a configured resource and checks its transaction domain and optional kind.</summary>
    /// <param name="view">Current gated storage view.</param>
    /// <param name="partition">Atomic partition containing the operation.</param>
    /// <param name="name">Resource identifier.</param>
    /// <param name="kind">Optional required resource kind.</param>
    /// <returns>The persisted resource definition.</returns>
    public ResourceDefinition Resource(IKeyValueView view, PartitionRef partition, string name, ResourceKind? kind = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(partition);
        var resource = view.GetRecord<ResourceDefinition>(KeySpace.Resource(partition.TenantId, partition.DatabaseId, name))
            ?? throw Errors.Fail(ErrorCode.NotFound, "The resource is not configured.");
        if (resource.TransactionDomainId != partition.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Conflict, "The resource belongs to a different transaction domain.");
        }

        if (kind is { } required && resource.Kind != required)
        {
            throw Errors.Fail(ErrorCode.Validation, "The resource has a different kind.");
        }

        return resource;
    }

    /// <summary>Creates the commit token for one atomic partition and applied position.</summary>
    /// <param name="partition">Complete atomic partition identity.</param>
    /// <param name="position">Applied commit position.</param>
    /// <returns>The token scoped to the current store incarnation.</returns>
    public CommitToken Token(PartitionRef partition, long position)
    {
        ArgumentNullException.ThrowIfNull(partition);
        return new(Store.Identity.Incarnation, partition.AtomicPartitionId, position, 1);
    }
    /// <summary>Validates every component of an atomic partition identity.</summary>
    /// <param name="partition">Complete identity to validate.</param>
    public static void ValidatePartition(PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        JsonData.Identifier(partition.TenantId);
        JsonData.Identifier(partition.DatabaseId);
        JsonData.Identifier(partition.TransactionDomainId);
        JsonData.Identifier(partition.PartitionKey);
    }

}
