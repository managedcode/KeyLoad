using KeyLoad.Core;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using ZoneTree.FullTextSearch.Index;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextIncrementalNativeOwner : IDisposable
{
    private readonly string root;
    private readonly string leaf;
    private readonly Guid sourceNodeId;
    private readonly IOptions<NativeTextExecutionOptions> executionOptions;
    private IndexOfTokenRecordPreviousToken<ulong, ulong>? index;
    private bool disposed;
    private bool postingObserved;
    private readonly Action<NativeTextFaultStage>? faultObserver;
    private readonly Action? postingBoundary;
    private readonly Action? deletionBoundary;
    private readonly Action? additionBoundary;
    private bool deletionObserved;
    private bool additionObserved;

    internal NativeTextIncrementalNativeOwner(string root, string leaf, Guid sourceNodeId,
        IOptions<NativeTextExecutionOptions> executionOptions, Action<NativeTextFaultStage>? faultObserver = null)
    {
        this.root = root;
        this.leaf = leaf;
        this.sourceNodeId = sourceNodeId;
        this.executionOptions = executionOptions;
        this.faultObserver = faultObserver;
        postingBoundary = faultObserver is null ? null : ObservePosting;
        deletionBoundary = faultObserver is null ? null : ObserveDeletion;
        additionBoundary = faultObserver is null ? null : ObserveAddition;
    }

    internal string Root => root;
    internal string Leaf => leaf;
    internal string Path => System.IO.Path.Combine(root, leaf);

    internal void Open(ReadExecutionBudget budget)
    {
        budget.Check();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (index is not null)
        { throw NativeTextErrors.Ownership(); }
        var owner = NativeTextOwnerFiles.ReadOwner(System.IO.Path.Combine(Path, NativeTextProtocol.OwnerFile),
            root, leaf, sourceNodeId, executionOptions);
        NativeTextOwnedInventory.ValidateTrackedLayout(Path, owner.OwnedPaths, budget,
            allowMissingNative: true, executionOptions: executionOptions);
        index = NativeTextIndex.Open(System.IO.Path.Combine(Path, NativeTextProtocol.NativeDirectory),
            new NativeTextFileStreamProvider(root, leaf, sourceNodeId, executionOptions), executionOptions);
        budget.Check();
    }

    internal void ApplyIntent(NativeTextIncrementalIntent intent, long maximumIntentBytes, ReadExecutionBudget budget)
    {
        RequirePersistedIntent(intent, maximumIntentBytes, budget);
        var current = index ?? throw NativeTextErrors.Ownership();
        foreach (var change in intent.Changes)
        {
            budget.Check();
            NativeTextIncrementalPostingWriter.Apply(current, change.Removals, change.Additions, budget,
                postingObserved ? null : postingBoundary, deletionObserved ? null : deletionBoundary,
                additionObserved ? null : additionBoundary);
        }
        budget.Check();
    }

    internal NativeTextFile[] CloseAndCapture(ReadExecutionBudget budget)
    {
        Close();
        budget.Check();
        var owner = NativeTextOwnerFiles.ReadOwner(System.IO.Path.Combine(Path, NativeTextProtocol.OwnerFile),
            root, leaf, sourceNodeId, executionOptions);
        var files = NativeTextInventory.Capture(Path, owner.OwnedPaths, executionOptions, budget);
        NativeTextFiles.CheckGenerationBound(Path, executionOptions, budget);
        budget.Check();
        return files;
    }

    private void RequirePersistedIntent(NativeTextIncrementalIntent expected, long maximumBytes,
        ReadExecutionBudget budget)
    {
        var actual = NativeTextIncrementalMetadata.ReadIntent(Path, maximumBytes, budget);
        budget.ChargeBytes(NativeSerialization.Measure(expected));
        budget.ChargeBytes(NativeSerialization.Measure(actual));
        var expectedBytes = NativeSerialization.Serialize(expected);
        var actualBytes = NativeSerialization.Serialize(actual);
        budget.Check();
        if (!expectedBytes.AsSpan().SequenceEqual(actualBytes))
        { throw NativeTextErrors.Corrupt(); }
    }

    internal Action<NativeTextFaultStage>? FaultObserver => faultObserver;

    internal void Observe(NativeTextFaultStage stage) => faultObserver?.Invoke(stage);

    private void ObservePosting()
    {
        if (postingObserved)
        { return; }
        postingObserved = true;
        Observe(NativeTextFaultStage.NativePostingWritten);
    }

    private void ObserveDeletion()
    {
        if (deletionObserved)
        { return; }
        deletionObserved = true;
        Observe(NativeTextFaultStage.NativeDeletionWritten);
    }

    private void ObserveAddition()
    {
        if (additionObserved)
        { return; }
        additionObserved = true;
        Observe(NativeTextFaultStage.NativeAdditionWritten);
    }

    internal void CloseAfterOperation() => Close();

    public void Dispose()
    {
        if (disposed)
        { return; }
        Close();
        disposed = true;
    }

    private void Close()
    {
        if (index is not { } current)
        { return; }
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(current.EvictToDisk, failures);
        ServerFailureObserver.Observe(current.WaitForBackgroundThreads, failures);
        ServerFailureObserver.Observe(current.ZoneTree1.Maintenance.SaveMetaData, failures);
        var beforeDispose = failures.Count;
        try
        { index.Dispose(); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        if (failures.Count == beforeDispose)
        { index = null; }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
