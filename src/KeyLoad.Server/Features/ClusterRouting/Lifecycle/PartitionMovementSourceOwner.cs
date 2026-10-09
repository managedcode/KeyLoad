using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Owns node-local source sessions; no handle survives physical runtime shutdown.</summary>
internal sealed partial class PartitionMovementSourceOwner : INativePartitionMovementCapture, IAsyncDisposable
{
    private const string ClosedDetail = "The partition movement source capability is unavailable.";
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, IPartitionMovementRetainedSourceImage> sessions = [];
    private readonly Dictionary<Guid, IPartitionMovementPendingSourceRead> captures = [];
    private readonly Dictionary<PartitionMovementSourceScope, IPartitionMovementRetainedSourceImage[]> closedMoves = [];
    private readonly Dictionary<PartitionMovementSourceScope, NativeRequestWorkLease> closedAdmissions = [];
    private readonly PartitionMovementSourceClosures closures;
    private readonly PartitionMovementTransferSourceReads transfers;
    private readonly PartitionMovementSourceImageLookup images;
    private readonly DatabaseEngine database;
    private readonly ICacheMemoryBudget memory;
    private readonly NativeRequestWorkOwner workOwner;
    private readonly CancellationTokenSource stopping = new();
    private readonly IOptions<DatabaseLimits> limits;
    private readonly TimeProvider clock;
    private readonly Func<PartitionMovePeerEnvelope, CancellationToken, Task<PartitionMovePhaseResult>> settle;
    private bool closing;
    private Task? shutdown;

    internal PartitionMovementSourceOwner(DatabaseEngine database, ICacheMemoryBudget memory,
        IOptions<DatabaseLimits> limits, NativeRequestWorkOwner workOwner, TimeProvider clock,
        Func<PartitionMovePeerEnvelope, CancellationToken, Task<PartitionMovePhaseResult>> settle)
    {
        this.database = database;
        this.memory = memory;
        this.workOwner = workOwner;
        this.limits = limits;
        this.clock = clock;
        this.settle = settle;
        images = new(gate, sessions, RequireMoveOpen, clock);
        closures = new(gate, sessions, captures, closedMoves, closedAdmissions, workOwner, RequireOpen);
        transfers = new(gate, sessions, captures, closedMoves, database, memory, workOwner, RequireOpen,
            RequireMoveOpen, stopping.Token);
    }

    public async Task<PartitionMovementCaptureHandle> CaptureAsync(PrincipalRecord principal,
        PartitionMovePeerEnvelope verified, CancellationToken cancellationToken)
    {
        var commandId = RequireGrant(verified);
        PartitionMovementPendingCapture? producer = null;
        Task<PartitionMovementCaptureHandle>? retained = null;
        PartitionMovementSourceEntry? existing = null;
        lock (gate)
        {
            RequireMoveOpen(verified.Partition, verified.MoveId);
            if (captures.TryGetValue(commandId, out var pending))
            {
                if (pending is not PartitionMovementPendingCapture originalCapture)
                { throw Errors.Fail(ErrorCode.Conflict, ClosedDetail); }
                retained = originalCapture.Completion.Task;
            }
            else
            {
                existing = sessions.Values.OfType<PartitionMovementSourceEntry>().FirstOrDefault(value => value.CommandId == commandId);
                if (existing is null)
                {
                    producer = new(verified.Partition, verified.MoveId);
                    captures.Add(commandId, producer);
                }
            }
        }
        if (retained is not null || existing is not null)
        {
            var handle = existing?.Handle
                ?? await retained!.WaitAsync(cancellationToken).ConfigureAwait(false);
            var current = images.Find(principal, verified, handle.HandleId, cancellationToken);
            var work = new ReadExecutionBudget(limits, clock, cancellationToken);
            database.ValidateVerifiedPartitionMovementCaptureScope(principal.Id, verified, work);
            current.Session.RequireOpen();
            work.CheckResult(handle);
            return handle;
        }
        var original = producer ?? throw Errors.Fail(ErrorCode.Corruption, ClosedDetail);
        CompleteCapture(principal, verified, original, cancellationToken);
        return await original.Completion.Task.ConfigureAwait(false);
    }

    private void CompleteCapture(PrincipalRecord principal, PartitionMovePeerEnvelope verified,
        PartitionMovementPendingCapture producer, CancellationToken cancellationToken)
    {
        var (completed, failure) = PartitionMovementCaptureProducer.Run(database, memory, workOwner,
            limits, clock, principal, verified, producer, (handle, session, retainedWork) =>
            {
                lock (gate)
                {
                    RequireMoveOpen(verified.Partition, verified.MoveId);
                    sessions.Add(handle.HandleId, new PartitionMovementSourceEntry(verified, handle, session, retainedWork));
                }
            }, stopping.Token, cancellationToken);
        try
        { producer.Dispose(); }
        catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
        { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
        catch (Exception cleanup) when (!NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
        { failure = failure is null ? cleanup : new AggregateException(failure, cleanup); }
        lock (gate)
        {
            var completion = producer.Completion;
            if (failure is not null)
            { completion.TrySetException(failure); }
            else if (completed is not null)
            { completion.TrySetResult(completed); }
            else
            { completion.TrySetException(Errors.Fail(ErrorCode.Corruption, ClosedDetail)); }
            captures.Remove(RequireGrant(verified));
        }
    }

    internal INativePartitionMovementTransferRead TransferReads => transfers;

    internal Task CloseMoveAsync(PartitionRef partition, Guid moveId) => closures.CloseMoveAsync(partition, moveId);
    internal void RequireMoveJoined(PartitionRef partition, Guid moveId) => closures.RequireMoveJoined(partition, moveId);
    internal void ConfirmMoveClosed(PartitionRef partition, Guid moveId) => closures.ConfirmMoveClosed(partition, moveId);

    private static Guid RequireGrant(PartitionMovePeerEnvelope verified)
        => verified.Grant?.PhaseCommandId is { } id && id != Guid.Empty ? id
            : throw Errors.Fail(ErrorCode.OwnershipLost, ClosedDetail);

    private void RequireMoveOpen(PartitionRef partition, Guid moveId)
    {
        RequireOpen();
        if (closedMoves.ContainsKey(new(partition, moveId)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, ClosedDetail); }
    }

    private void RequireOpen()
    {
        if (closing)
        { throw Errors.Fail(ErrorCode.OwnershipLost, ClosedDetail); }
    }
}
