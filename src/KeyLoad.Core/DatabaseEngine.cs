using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.RuntimeJournaling.Contracts;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;
/// <summary>Owns the shared database execution context, policy snapshots and node-local read admission.</summary>
public sealed class DatabaseEngine
{
    internal MessagingExecutionOptions MessagingExecution => messagingExecution;
    internal EventSourceExecutionOptions EventSourceExecution => eventSourceExecution;
    internal TimeSeriesExecutionOptions TimeSeriesExecution => timeSeriesExecution;
    internal ChangeFeedExecutionOptions ChangeFeedExecution => changeFeedExecution;
    internal bool visibilityConfigured;
    internal RowVisibilityCache? RowVisibility { get; set; }

    /// <summary>Creates a node-owned engine with centrally validated operation policy snapshots.</summary>
    /// <param name = "store">The externally owned node-local atomic store.</param>
    /// <param name = "authorization">Evaluator for persisted operation, row and field policy.</param>
    /// <param name = "limits">Centrally validated operation resource limits.</param>
    /// <param name = "dueWorkOptions">Centrally validated node-local due discovery policy.</param>
    /// <param name = "eventSourceOptions">Centrally validated event continuation policy.</param>
    /// <param name = "messagingOptions">Centrally validated topic, catch-up and retry work limits.</param>
    /// <param name = "graphOptions">Centrally validated shared graph traversal limits.</param>
    /// <param name = "changeFeedOptions">Centrally validated change cursor and projection policy.</param>
    /// <param name = "claimsOptions">Centrally validated execution signed-claim decoding limits.</param>
    /// <param name = "timeSeriesOptions">Centrally validated time-series append admission.</param>
    /// <param name = "timeProvider">Optional business clock; hosting runtime time is unaffected.</param>
    public DatabaseEngine(IAtomicStore store, IAuthorizationPolicy authorization, IOptions<DatabaseLimits> limits, IOptions<DueWorkExecutionOptions> dueWorkOptions, IOptions<EventSourceExecutionOptions> eventSourceOptions, IOptions<MessagingExecutionOptions> messagingOptions, IOptions<GraphExecutionOptions> graphOptions, IOptions<ChangeFeedExecutionOptions> changeFeedOptions, IOptions<ClaimsExecutionOptions> claimsOptions, IOptions<TimeSeriesExecutionOptions> timeSeriesOptions, TimeProvider? timeProvider = null)
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
        ArgumentNullException.ThrowIfNull(claimsOptions);
        ClaimsExecution = claimsOptions.Value;
        ClaimsExecution.Validate();
        ArgumentNullException.ThrowIfNull(timeSeriesOptions);
        TimeSeriesOptions = timeSeriesOptions;
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
        Authorization = authorization;
        Limits = operationLimits;
        analyticalReadGate = new(operationLimits.MaxConcurrentQueries);
        DueDiscoveryDeadline = dueExecution.DiscoveryDeadline;
        eventSourceCursorLifetime = eventSourceExecution.CursorLifetime;
        Clock = timeProvider ?? TimeProvider.System;
        Durability = store.Identity.Durability;
    }

    private readonly ChangeFeedExecutionOptions changeFeedExecution;
    internal ClaimsExecutionOptions ClaimsExecution { get; }

    private readonly TimeSeriesExecutionOptions timeSeriesExecution;
    internal IOptions<TimeSeriesExecutionOptions> TimeSeriesOptions { get; }

    private readonly EventSourceExecutionOptions eventSourceExecution;
    private readonly DueWorkExecutionOptions dueExecution;
    internal DueWorkExecutionOptions DueExecution => dueExecution;

    private readonly MessagingExecutionOptions messagingExecution;
    private readonly GraphExecutionOptions graphExecution;
    internal IOptions<DatabaseLimits> OperationLimitsOptions { get; }
    internal IOptions<GraphExecutionOptions> GraphOptions { get; }
    internal GraphExecutionOptions GraphExecution => graphExecution;

    internal readonly TimeSpan eventSourceCursorLifetime;
    internal TimeProvider Clock { get; }
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
    internal int AcknowledgedPersistedCopies => Durability == DurabilityProfile.Buffered ? EngineResourceAccess.NoPersistedCopies : CommitReceipt.LocalCopyCount;
    /// <summary>Gets the canonical store's last applied replicated position.</summary>
    public long LastApplied => Store.Read(view => view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes ? OrleansSerialization.Deserialize<long>(bytes) : EngineResourceAccess.DatabaseEngineNoRetainedBytes);

    internal readonly AnalyticalReadGate analyticalReadGate;
    /// <summary>Gets the number of currently admitted analytical operations on this engine.</summary>
    public int QueryReadsInFlight => analyticalReadGate.InFlight;

    internal RuntimeJournalOperations? configuredRuntimeJournal;
    internal RuntimeJournalOperations RuntimeJournal => Volatile.Read(ref configuredRuntimeJournal) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.NotConfigured);
}
