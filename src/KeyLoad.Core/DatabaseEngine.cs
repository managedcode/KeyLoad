using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

/// <summary>Applies authorized database commands and reads through one node-owned atomic store.</summary>
public sealed partial class DatabaseEngine
{
    private const int DatabaseEngineNoRetainedBytes = 0;
    private const string DatabaseEngineResourceIsNotConfiguredDetail = "The resource is not configured.";
    private const string DatabaseEngineResourceBelongsToADifferentTransactionDomainDetail = "The resource belongs to a different transaction domain.";
    private const string DatabaseEngineResourceHasADifferentKindDetail = "The resource has a different kind.";
    private const int DatabaseEngineMinimumPositiveCount = 1;

    /// <summary>Creates a node-owned engine with centrally validated operation policy snapshots.</summary>
    /// <param name="store">The externally owned node-local atomic store.</param>
    /// <param name="authorization">Evaluator for persisted operation, row and field policy.</param>
    /// <param name="limits">Centrally validated operation resource limits.</param>
    /// <param name="dueWorkOptions">Centrally validated node-local due discovery policy.</param>
    /// <param name="eventSourceOptions">Centrally validated event continuation policy.</param>
    /// <param name="messagingOptions">Centrally validated topic, catch-up and retry work limits.</param>
    /// <param name="graphOptions">Centrally validated shared graph traversal limits.</param>
    /// <param name="changeFeedOptions">Centrally validated change cursor and projection policy.</param>
    /// <param name="blobOptions">Centrally validated blob restore and catalog proof policy.</param>
    /// <param name="claimsOptions">Centrally validated native signed-claim decoding limits.</param>
    /// <param name="timeSeriesOptions">Centrally validated time-series append admission.</param>
    /// <param name="physicalOwner">Optional immutable configured owner enabling explicit multi-owner local execution fences.</param>
    /// <param name="timeProvider">Optional business clock; hosting runtime time is unaffected.</param>
    public DatabaseEngine(IAtomicStore store, IAuthorizationPolicy authorization, IOptions<DatabaseLimits> limits,
        IOptions<DueWorkExecutionOptions> dueWorkOptions, IOptions<EventSourceExecutionOptions> eventSourceOptions,
        IOptions<MessagingExecutionOptions> messagingOptions, IOptions<GraphExecutionOptions> graphOptions,
        IOptions<ChangeFeedExecutionOptions> changeFeedOptions, IOptions<BlobExecutionOptions> blobOptions,
        IOptions<NativeClaimsExecutionOptions> claimsOptions,
        IOptions<TimeSeriesExecutionOptions> timeSeriesOptions,
        TimeProvider? timeProvider = null, PhysicalShardRecord? physicalOwner = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(dueWorkOptions);
        ArgumentNullException.ThrowIfNull(eventSourceOptions);
        ArgumentNullException.ThrowIfNull(messagingOptions);
        messagingExecution = messagingOptions.Value;
        messagingExecution.Validate();
        ArgumentNullException.ThrowIfNull(graphOptions);
        graphExecution = graphOptions.Value;
        graphExecution.Validate();
        ArgumentNullException.ThrowIfNull(changeFeedOptions);
        changeFeedExecution = changeFeedOptions.Value;
        changeFeedExecution.Validate();
        ArgumentNullException.ThrowIfNull(blobOptions);
        BlobExecution = blobOptions.Value;
        BlobExecution.Validate();
        ArgumentNullException.ThrowIfNull(claimsOptions);
        ClaimsExecution = claimsOptions.Value;
        ClaimsExecution.Validate();
        ArgumentNullException.ThrowIfNull(timeSeriesOptions);
        timeSeriesExecution = timeSeriesOptions.Value;
        timeSeriesExecution.Validate();
        OperationLimitsOptions = limits;
        GraphOptions = graphOptions;
        var operationLimits = limits.Value;
        dueExecution = dueWorkOptions.Value;
        eventSourceExecution = eventSourceOptions.Value;
        operationLimits.Validate();
        dueExecution.Validate();
        eventSourceExecution.Validate();
        Store = store;
        configuredPhysicalOwner = physicalOwner;
        if (physicalOwner is not null && physicalOwner.Incarnation != store.Identity.Incarnation)
        { throw Errors.Fail(ErrorCode.OwnershipLost, ForeignPlacementExecution); }
        Authorization = authorization;
        Limits = operationLimits;
        ValidateMovementRestoration();
        analyticalReadGate = new(operationLimits.MaxConcurrentQueries);
        DueDiscoveryDeadline = dueExecution.DiscoveryDeadline;
        eventSourceCursorLifetime = eventSourceExecution.CursorLifetime;
        Clock = timeProvider ?? TimeProvider.System;
        Durability = store.Identity.Durability;
    }

    private readonly PhysicalShardRecord? configuredPhysicalOwner;
    private readonly ChangeFeedExecutionOptions changeFeedExecution;
    internal BlobExecutionOptions BlobExecution { get; }
    internal NativeClaimsExecutionOptions ClaimsExecution { get; }
    private readonly TimeSeriesExecutionOptions timeSeriesExecution;
    private readonly EventSourceExecutionOptions eventSourceExecution;
    private readonly DueWorkExecutionOptions dueExecution;
    internal DueWorkExecutionOptions DueExecution => dueExecution;
    private readonly MessagingExecutionOptions messagingExecution;
    private readonly GraphExecutionOptions graphExecution;
    internal IOptions<DatabaseLimits> OperationLimitsOptions { get; }
    internal IOptions<GraphExecutionOptions> GraphOptions { get; }
    internal GraphExecutionOptions GraphExecution => graphExecution;
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
    public long LastApplied => Store.Read(view => view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes ? NativeSerialization.Deserialize<long>(bytes) : DatabaseEngineNoRetainedBytes);

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
        RequireNoUnpublishedPartitionMoveTarget(view, partition);
        RequireLocalResourceOwner(view, partition);
        var resource = view.GetRecord<ResourceDefinition>(KeySpace.Resource(partition.TenantId, partition.DatabaseId, name))
            ?? throw Errors.Fail(ErrorCode.NotFound, DatabaseEngineResourceIsNotConfiguredDetail);
        if (resource.TransactionDomainId != partition.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Conflict, DatabaseEngineResourceBelongsToADifferentTransactionDomainDetail);
        }

        if (kind is { } required && resource.Kind != required)
        {
            throw Errors.Fail(ErrorCode.Validation, DatabaseEngineResourceHasADifferentKindDetail);
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
            || token.Position < DatabaseEngineMinimumPositiveCount || token.OwnershipEpoch != owner.PlacementEpoch)
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
