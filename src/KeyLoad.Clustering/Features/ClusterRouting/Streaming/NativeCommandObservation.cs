using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>One bounded admission retains its pre-effect budget and exact original submission intent.</summary>
internal sealed class NativeCommandObservation
{
    private readonly NativeRequestWorkOwner owner;
    private readonly DatabaseEngine database;
    private readonly NativeRequestWorkLease commandProducer;
    private ReplicatedOperation? intent;
    private NativeCommandObservationHandleV1? handle;
    private readonly NativeCommandObservationResources resources;
    private readonly TaskCompletionSource initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource preparationFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource readerFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly NativeCommandObservationCommand command = new();
    private readonly NativeCommandObservationSettlement completion;
    private Task? commandCompletion;
    private NativeCommandObservationStage stage;
    private Task? settlement;
    internal NativeRequestWorkLease Lease
    {
        get;
    }
    internal ReplicatedOperation Intent => Volatile.Read(ref intent)
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
    internal NativeCommandObservationHandleV1 Handle => Volatile.Read(ref handle)
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
    internal string SignedToken { get; private set; } = string.Empty;
    internal ReadExecutionBudget Budget => resources.Budget;
    internal CancellationToken Token => resources.Token;
    internal ImmutableArray<Exception> TerminalFailures { get; set; } = [];
    internal DecodedGrainRequest Request
    {
        get; private set;
    }
    internal NativeCapabilityWorkLifetime CommandWork => command.Work;
    internal bool OwnsCommandWork(NativeCapabilityWorkLifetime work) => command.Owns(work);

    internal NativeCommandObservation(NativeRequestWorkOwner owner, NativeRequestWorkLease lease,
        NativeCommandObservationResources resources, DatabaseEngine database, NativeRequestWorkLease commandProducer)
    {
        this.owner = owner;
        this.resources = resources;
        this.database = database;
        this.commandProducer = commandProducer;
        Lease = lease;
        completion = new(owner, this, resources, initialized.Task, preparationFinished.Task);
    }

    internal void Initialize(DecodedGrainRequest verified, NativeCommandObservationAdmissionV1 admission,
        PhysicalShardRecord actualOwner, ReplicaSiloDiscovery actualRuntime, TimeProvider clock,
        CancellationToken callerToken)
    {
        resources.Initialize(this, Lease, admission, verified.Envelope.ExpiresAt, clock, owner.ShutdownToken, callerToken);
        Budget.Check();
        Budget.CheckResult(verified.Envelope);
        var payload = verified.Payload.ToArray();
        Request = new(verified.Envelope with
        {
            Payload = payload
        }, payload);
        handle = NativeCommandObservationIdentity.Create(Request, actualOwner, actualRuntime, database);
        Budget.CheckResult(Handle);
        SignedToken = database.Sign(Handle);
        Budget.CheckResult(SignedToken);
        Budget.Check();
        FinishInitialization();
    }

    internal void AttachIntent(ReplicatedOperation genuineIntent, NativeCapabilityWorkLifetime commandWork)
    {
        Budget.Check();
        NativeCommandObservationIdentity.RequireIntent(Handle, genuineIntent, database);
        Budget.CheckResult(genuineIntent);
        owner.Observations.AttachIntent(this, genuineIntent, commandWork);
        Budget.Check();
    }

    internal void RequireIntentMissingAtGate(NativeRequestWorkLease command)
    {
        if (stage != NativeCommandObservationStage.Reserved || intent is not null
            || !ReferenceEquals(commandProducer, command))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
    }

    internal void AttachIntentAtGate(ReplicatedOperation genuineIntent, NativeCapabilityWorkLifetime commandWork)
    {
        command.AttachAtGate(commandWork);
        intent = genuineIntent;
        stage = NativeCommandObservationStage.Prepared;
    }

    internal void BeginCommandAtGate()
    {
        if (stage != NativeCommandObservationStage.Prepared || !preparationFinished.Task.IsCompleted)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
        Budget.Check();
        command.BeginAtGate();
        stage = NativeCommandObservationStage.CommandExecuting;
    }

    internal Task FinishCommandExecutionAsync(Exception? originalFailure)
        => owner.Observations.FinishCommandExecutionAsync(this, originalFailure);

    internal (Task Task, TaskCompletionSource? Start) SelectCommandCompletionAtGate(Exception? original)
    {
        if (commandCompletion is not null)
        {
            return (commandCompletion, null);
        }
        if (stage is not (NativeCommandObservationStage.CommandExecuting or NativeCommandObservationStage.Settling))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
        var (task, commandStart) = command.SelectFinishAtGate(original, abort: false);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        commandCompletion = FinishCommandAsync(start.Task, task, commandStart);
        return (commandCompletion, start);
    }

    internal void ClearOwnedRequest() => NativeCommandObservationIdentity.ClearRequest(Request);

    internal void FinishPreparation() => preparationFinished.TrySetResult();

    internal void FinishInitialization() => initialized.TrySetResult();
    internal Task AbortAsync(Exception? original) => owner.SettleCommandObservation(this, original, null, abort: true);
    internal Task CompleteAsync(Exception? original, Exception? failure)
        => owner.SettleCommandObservation(this, original, failure, abort: false);
    internal void StartAbort()
    {
        var original = AbortAsync(null);
        _ = original.ContinueWith(ObserveAutomaticFailure, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static void ObserveAutomaticFailure(Task completed)
    {
        _ = completed.Exception;
    }

    internal void RequireIdentity(NativeCommandObservationHandleV1 supplied, PhysicalShardRecord actualOwner,
        ReplicaSiloDiscovery actualRuntime, DatabaseEngine database)
        => NativeCommandObservationIdentity.Require(supplied, Handle, actualOwner, actualRuntime, database);

    internal bool MarkSubmittedAtGate()
    {
        if (stage != NativeCommandObservationStage.CommandExecuting)
        {
            return false;
        }
        stage = NativeCommandObservationStage.SubmitSettled;
        return true;
    }

    internal void MarkSettledAtGate() => stage = NativeCommandObservationStage.Settled;

    internal void ConsumeAtGate()
    {
        if (stage != NativeCommandObservationStage.SubmitSettled || intent is null)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
        Budget.Check();
        stage = NativeCommandObservationStage.ReadExecuting;
    }

    internal (Task Task, TaskCompletionSource? Start) SelectSettlementAtGate(Exception? original,
        Exception? failure, bool abort)
    {
        completion.Record(original, failure);
        if (!abort)
        {
            readerFinished.TrySetResult();
        }
        if (settlement is not null)
        {
            return (settlement, null);
        }
        var reader = stage == NativeCommandObservationStage.ReadExecuting ? readerFinished.Task : Task.CompletedTask;
        stage = NativeCommandObservationStage.Settling;
        var (commandTask, commandStart) = command.SelectFinishAtGate(null, abort: true);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        settlement = completion.FinishAsync(start.Task, reader, commandTask, commandStart);
        return (settlement, start);
    }

    private async Task FinishCommandAsync(Task start, Task<NativeRequestWorkOriginalSettlement> original,
        TaskCompletionSource? commandStart)
    {
        await start.ConfigureAwait(false);
        commandStart?.TrySetResult();
        var result = await original.ConfigureAwait(false);
        if (result.OriginalFailure is null && result.CleanupFailures.IsEmpty
            && owner.Observations.MarkSubmitted(this))
        {
            return;
        }
        await AbortAsync(null).ConfigureAwait(false);
    }
}

internal enum NativeCommandObservationStage
{
    Reserved,
    Prepared,
    CommandExecuting,
    SubmitSettled,
    ReadExecuting,
    Settling,
    Settled
}
