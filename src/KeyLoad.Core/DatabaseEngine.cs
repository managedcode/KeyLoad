using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

/// <summary>Applies authorized database commands and reads through one node-owned atomic store.</summary>
public sealed partial class DatabaseEngine
{
    /// <summary>Creates a node-owned engine with centrally validated operation policy snapshots.</summary>
    /// <param name="store">The externally owned node-local atomic store.</param>
    /// <param name="authorization">Evaluator for persisted operation, row and field policy.</param>
    /// <param name="limits">Centrally validated operation resource limits.</param>
    /// <param name="dueWorkOptions">Centrally validated node-local due discovery policy.</param>
    /// <param name="eventSourceOptions">Centrally validated event continuation policy.</param>
    /// <param name="timeProvider">Optional business clock; hosting runtime time is unaffected.</param>
    public DatabaseEngine(IAtomicStore store, IAuthorizationPolicy authorization, IOptions<DatabaseLimits> limits,
        IOptions<DueWorkExecutionOptions> dueWorkOptions, IOptions<EventSourceExecutionOptions> eventSourceOptions,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(dueWorkOptions);
        ArgumentNullException.ThrowIfNull(eventSourceOptions);
        var operationLimits = limits.Value;
        var dueSettings = dueWorkOptions.Value;
        var eventSettings = eventSourceOptions.Value;
        operationLimits.Validate();
        dueSettings.Validate();
        eventSettings.Validate();
        Store = store;
        Authorization = authorization;
        Limits = operationLimits;
        DueDiscoveryDeadline = dueSettings.DiscoveryDeadline;
        eventSourceCursorLifetime = eventSettings.CursorLifetime;
        Clock = timeProvider ?? TimeProvider.System;
        Durability = store.Identity.Durability;
    }

    private readonly TimeSpan eventSourceCursorLifetime;
    private TimeProvider Clock { get; }
    internal TimeSpan DueDiscoveryDeadline { get; }
    /// <summary>Gets the borrowed business clock for consistent persisted read authority.</summary>
    public TimeProvider EvaluationClock => Clock;
    /// <summary>Gets the externally owned atomic store used by this engine.</summary>
    public IAtomicStore Store { get; }
    /// <summary>Gets the evaluator for persisted authorization policy.</summary>
    public IAuthorizationPolicy Authorization { get; }
    /// <summary>Gets the configured operation resource limits.</summary>
    public DatabaseLimits Limits { get; }
    /// <summary>Gets or sets the acknowledgement durability reported in commit receipts.</summary>
    public DurabilityProfile Durability { get; set; }
    /// <summary>Gets the canonical store's last applied replicated position.</summary>
    public long LastApplied => Store.Read(view => view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes ? NativeSerialization.Deserialize<long>(bytes) : 0);

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

    /// <summary>Creates a commit token from the placement witness in the command's existing native view.</summary>
    /// <param name="view">The command's already-owned transaction or committed read view.</param>
    /// <param name="partition">Complete atomic partition identity.</param>
    /// <param name="position">Applied commit position.</param>
    /// <returns>The token bound to the resolved incarnation and placement epoch.</returns>
    internal static CommitToken Token(IKeyValueView view, PartitionRef partition, long position)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(partition);
        var owner = ReadPlacementWitness(view, partition);
        return new(owner.Incarnation, partition.AtomicPartitionId, position, owner.PlacementEpoch);
    }

    internal static void ValidateCommitToken(IKeyValueView view, PartitionRef partition, CommitToken token,
        ErrorCode failureCode, string safeDetail)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(token);
        var owner = ReadPlacementWitness(view, partition);
        if (token.Incarnation != owner.Incarnation || token.AtomicPartitionId != partition.AtomicPartitionId
            || token.Position < 1 || token.OwnershipEpoch != owner.PlacementEpoch)
        {
            throw Errors.Fail(failureCode, safeDetail);
        }
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
