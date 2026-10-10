using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Core.Features.Search;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextOnlineSourcePin : IDisposable
{
    private const int NoFailures = 0;
    private const int AllocationOnlyRecords = 0;
    private const string InvalidPin = "The online text snapshot does not match its captured canonical source.";
    private const string AlreadyVisited = "The online text snapshot traversal is already settled.";
    private readonly Lock sync = new();
    private readonly ZoneTreeReadCutLease native;
    private readonly ReadExecutionBudget budget;
    private readonly ReadExecutionBudgetReadGrant grant;
    private readonly NativeTextSeedPin scope;
    private readonly NativeTextSeedCapture metadata;
    private bool visited;
    private bool disposed;

    private NativeTextOnlineSourcePin(ZoneTreeReadCutLease native, ReadExecutionBudget budget,
        ReadExecutionBudgetReadGrant grant, NativeTextSeedPin scope, NativeTextSeedCapture metadata)
    {
        this.native = native;
        this.budget = budget;
        this.grant = grant;
        this.scope = scope;
        this.metadata = metadata;
    }

    internal static NativeTextOnlineSourcePin Capture(DatabaseEngine database, string principalId,
        NativeTextSeedPin scope, ReadExecutionBudget budget)
    {
        NativeTextOnlineSourcePin? captured = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            captured = NativeTextSeedCollector.CaptureOnlinePinned(database, principalId, scope, budget,
                (raw, metadata) => CaptureNative(database, raw, scope, metadata, budget));
            captured.RequireSameCut();
            budget.Check();
        }, failures);
        if (failures.Count != NoFailures && captured is not null)
        { ServerFailureObserver.Observe(captured.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return captured!;
    }

    private static NativeTextOnlineSourcePin CaptureNative(DatabaseEngine database, IKeyValueView raw,
        NativeTextSeedPin scope, NativeTextSeedCapture metadata, ReadExecutionBudget budget)
    {
        if (database.Store is not ZoneTreeStore owner)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, InvalidPin); }
        var grant = budget.CreateReadGrant(budget.RemainingReadGrantBytes, budget.RemainingReadGrantRecords);
        var limits = new ZoneTreeReadCutLimits(grant.RemainingRecords, grant.RemainingBytes, budget.RemainingLifetime);
        ZoneTreeReadCutLease native;
        try
        {
            native = owner.CaptureNativeReadCut(raw, limits, budget.Cancellation);
        }
        catch (Exception failure)
        {
            // Native capture aggregates its primary and failed cleanup; uncertain ownership stays reserved.
            if (failure is AggregateException)
            { throw; }
            var failures = new List<Exception> { failure };
            ServerFailureObserver.Observe(() => budget.CompleteSettledReadGrant(grant), failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
        return new(native, budget, grant, scope, metadata);
    }

    internal NativeTextSeedCapture CapturedMetadata
    {
        get
        {
            lock (sync)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return metadata;
            }
        }
    }

    internal void AdmitRetainedBytes(long bytes)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            budget.ImportReadGrant(grant, bytes, AllocationOnlyRecords);
        }
    }

    internal NativeTextSeedCapture ReadSeed()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (visited)
            { throw Errors.Fail(ErrorCode.Conflict, AlreadyVisited); }
            visited = true;
            var reader = new NativeTextOnlineSeedReader(native, budget, grant, scope);
            return metadata with { Documents = reader.Read() };
        }
    }

    private void RequireSameCut()
    {
        var actual = native.Cut;
        if (actual.NodeId != scope.NodeId || actual.Incarnation != metadata.Incarnation
            || actual.FormatVersion != metadata.DataEpoch || actual.ReadGeneration != metadata.ReadGeneration
            || actual.Position != metadata.Position)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidPin); }
    }

    public void Dispose()
    {
        lock (sync)
        {
            native.EnsureDisposalCanJoin();
            if (disposed)
            { return; }
            native.Dispose();
            budget.CompleteSettledReadGrant(grant);
            disposed = true;
        }
    }
}
