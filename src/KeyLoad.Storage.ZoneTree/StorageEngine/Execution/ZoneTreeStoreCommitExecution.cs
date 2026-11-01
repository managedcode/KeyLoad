using ZoneTree;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Compiles transactional writes with native conflict retries and plain writes with ordered native WAL sync.</summary>
internal static class ZoneTreeStoreCommitExecution
{
    private const string AsyncRequired = "Non-transactional table writes require the asynchronous commit API.";
    private const string NestedCommit = "A commit cannot enter an already owned storage operation.";
    internal static T Commit<T>(ZoneTreeStoreRuntime runtime, Func<IAtomicTransaction, long, T> compile)
    {
        RequireOutsideOperation(runtime);
        long position = 0;
        var mutations = 0;
        var result = runtime.Tree.Execute(transaction =>
        {
            using var operation = new ZoneTreeOperationContext(runtime, transaction, ownsTransaction: false);
            var compiled = ZoneTreeCommitCompilation.Compile(runtime, compile);
            if (compiled.Changes.Any(change => ZoneTreeDataRouting.IsPlain(runtime, change.Key.Span)))
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, AsyncRequired);
            }
            position = compiled.Changes.Length > 0 ? compiled.Position : 0;
            mutations = compiled.Changes.Length;
            if (mutations > 0)
            {
                ZoneTreeCommitPublication.Stage(runtime, transaction, compiled.Changes, position);
            }
            return compiled.Result;
        });
        if (mutations > 0)
        {
            ZoneTreeCommitPublication.Committed(runtime, position, mutations);
        }
        return result;
    }

    internal static async Task<T> CommitAsync<T>(ZoneTreeStoreRuntime runtime,
        Func<IAtomicTransaction, long, T> compile, CancellationToken cancellationToken)
    {
        RequireOutsideOperation(runtime);
        using var transaction = runtime.Tree.BeginTransaction();
        using (var operation = new ZoneTreeOperationContext(runtime, transaction, ownsTransaction: false))
        {
            var compiled = ZoneTreeCommitCompilation.Compile(runtime, compile);
            cancellationToken.ThrowIfCancellationRequested();
            if (compiled.Changes.Length == 0)
            {
                return compiled.Result;
            }
            var plain = compiled.Changes.Where(change => ZoneTreeDataRouting.IsPlain(runtime, change.Key.Span)).ToArray();
            if (plain.Length > 0)
            {
                var metadata = compiled.Changes.Where(change => !ZoneTreeDataRouting.IsPlain(runtime, change.Key.Span)).ToArray();
                await ZoneTreePlainPublication.PublishAsync(runtime, transaction, plain, metadata, compiled.Position);
                return compiled.Result;
            }
            ZoneTreeCommitPublication.Stage(runtime, transaction, compiled.Changes, compiled.Position);
            if (transaction.TryCommit())
            {
                ZoneTreeCommitPublication.Committed(runtime, compiled.Position, compiled.Changes.Length);
                return compiled.Result;
            }
        }
        // A purely transactional body has no external effects and uses the engine's own conflict retry API.
        return Commit(runtime, compile);
    }

    private static void RequireOutsideOperation(ZoneTreeStoreRuntime runtime)
    {
        if (runtime.Operation.Value is not null)
        {
            throw new InvalidOperationException(NestedCommit);
        }
    }

    /// <summary>Waits for ZoneTree's write-ahead log sync, which covers every commit published before the call. A failed
    /// sync leaves those outcomes unknown, so the store refuses work until it is reopened.</summary>
    internal static async Task SyncAsync(ZoneTreeStoreRuntime runtime, CancellationToken cancellationToken)
    {
        runtime.Check();
        var covered = runtime.Position;
        try
        {
            await runtime.PlainTree.SyncWALAsync(cancellationToken);
            await runtime.Tree.SyncWALAsync(cancellationToken);
            runtime.Options.FaultObserver?.Invoke(CommitStage.WriteAheadLogSynced, covered, 0);
        }
        catch (Exception original) when (original is not OperationCanceledException)
        {
            runtime.Poisoned = true;
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, ZoneTreePersistenceFormat.UnknownWriteOutcome, original);
        }
    }
}
