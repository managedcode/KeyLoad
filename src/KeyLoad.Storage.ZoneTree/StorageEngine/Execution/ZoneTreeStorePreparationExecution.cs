namespace KeyLoad.Storage.ZoneTree;

/// <summary>Owns one native snapshot while preparation mutations stay in a discard-only child.</summary>
internal static class ZoneTreeStorePreparationExecution
{
    private const int NextPosition = 1;
    internal const string OutsideTransactionCallback = "Transaction preparation requires its original synchronous compiler callback.";
    private const string Nested = "Store preparation cannot enter an already owned native operation.";
    private const string MissingSnapshot = "Transaction preparation requires its owning native snapshot.";

    internal static T Prepare<T>(ZoneTreeStoreRuntime runtime, Func<IAtomicTransaction, long, T> prepare)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        if (runtime.Operation.Value is not null)
        {
            throw new InvalidOperationException(Nested);
        }
        using var operation = new ZoneTreeOperationContext(runtime, runtime.Tree.BeginReadOnlyTransaction(), ownsTransaction: true);
        var position = checked(runtime.Position + NextPosition);
        return Fork(runtime, [], [], child => prepare(child, position));
    }

    /// <summary>Runs discard-only preparation over the parent's staged image.</summary>
    /// <param name="runtime">The owning store runtime.</param>
    /// <param name="earlier">Entries of the parent's earlier actions, visible but outside the child's frame budget.</param>
    /// <param name="action">The parent's current action entries, which the child's frame budget includes.</param>
    /// <param name="prepare">The synchronous preparation callback.</param>
    /// <param name="requireWriteTransaction">Whether the caller must own a native write transaction.</param>
    internal static T Fork<T>(ZoneTreeStoreRuntime runtime, IEnumerable<ZoneTreeStagedEntry> earlier,
        IEnumerable<ZoneTreeStagedEntry> action, Func<IAtomicTransaction, T> prepare, bool requireWriteTransaction = false)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        var context = runtime.Operation.Value;
        if (context is null || requireWriteTransaction && !context.Writable)
        {
            throw new InvalidOperationException(MissingSnapshot);
        }
        runtime.Check();
        return new ZoneTreePreparationTransaction(runtime, earlier, action).Run(prepare);
    }
}
