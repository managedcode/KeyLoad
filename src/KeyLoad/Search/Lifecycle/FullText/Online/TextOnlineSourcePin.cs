using KeyLoad.Core;
using KeyLoad.Core.Search;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Server.Search;

internal sealed class TextOnlineSourcePin : IDisposable
{
    private const int NoFailures = 0;
    private const string InvalidPin = "The online text snapshot does not match its captured canonical source.";
    private const string AlreadyVisited = "The online text snapshot traversal is already settled.";
    private readonly Lock sync = new();
    private readonly ZoneTreeReadCutLease execution;
    private readonly ReadExecutionBudget budget;
    private readonly TextSeedPin scope;
    private readonly TextSeedCapture metadata;
    private bool visited;
    private bool disposed;

    private TextOnlineSourcePin(ZoneTreeReadCutLease execution, ReadExecutionBudget budget, TextSeedPin scope, TextSeedCapture metadata)
    {
        this.execution = execution;
        this.budget = budget;
        this.scope = scope;
        this.metadata = metadata;
    }

    internal static TextOnlineSourcePin Capture(DatabaseEngine database, string principalId,
        TextSeedPin scope, ReadExecutionBudget budget)
    {
        TextOnlineSourcePin? captured = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            captured = TextSeedCollector.CaptureOnlinePinned(database, principalId, scope, budget,
                (raw, metadata) => RuntimeCapture(database, raw, scope, metadata, budget));
            captured.RequireSameCut();
            budget.Check();
        }, failures);
        if (failures.Count != NoFailures && captured is not null)
        {
            ServerFailureObserver.Observe(captured.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return captured!;
    }

    private static TextOnlineSourcePin RuntimeCapture(DatabaseEngine database, IKeyValueView raw,
        TextSeedPin scope, TextSeedCapture metadata, ReadExecutionBudget budget)
    {
        if (database.Store is not ZoneTreeStore owner)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, InvalidPin);
        }
        var limits = owner.BoundReadCut(int.MaxValue, long.MaxValue, budget.RemainingLifetime);
        ZoneTreeReadCutLease execution;
        try
        {
            execution = owner.CaptureReadCut(raw, limits, budget.Cancellation);
        }
        catch (Exception failure)
        {
            // Execution capture aggregates its primary and failed cleanup; uncertain ownership stays reserved.
            if (failure is AggregateException)
            {
                throw;
            }
            throw;
        }
        return new(execution, budget, scope, metadata);
    }

    internal TextSeedCapture CapturedMetadata
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

    internal TextSeedCapture ReadSeed()
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (visited)
            {
                throw Errors.Fail(ErrorCode.Conflict, AlreadyVisited);
            }
            visited = true;
            var reader = new TextOnlineSeedReader(execution, scope);
            return metadata with
            {
                Documents = reader.Read()
            };
        }
    }

    private void RequireSameCut()
    {
        var actual = execution.Cut;
        if (actual.NodeId != scope.NodeId || actual.Incarnation != metadata.Incarnation
            || actual.FormatVersion != metadata.DataEpoch || actual.ReadGeneration != metadata.ReadGeneration
            || actual.Position != metadata.Position)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidPin);
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            execution.EnsureDisposalCanJoin();
            if (disposed)
            {
                return;
            }
            execution.Dispose();
            disposed = true;
        }
    }
}
