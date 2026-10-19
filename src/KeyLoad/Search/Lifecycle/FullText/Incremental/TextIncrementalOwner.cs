using KeyLoad.Core;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;
using ZoneTree.FullTextSearch.Index;

namespace KeyLoad.Server.Features.Search;

internal sealed class TextIncrementalOwner : IDisposable
{
    private readonly string root;
    private readonly TextResourceOwnership? resources;
    private readonly string leaf;
    private readonly Guid sourceNodeId;
    private readonly IOptions<TextExecutionOptions> executionOptions;
    private IndexOfTokenRecordPreviousToken<ulong, ulong>? index;
    private bool disposed;
    private bool postingObserved;
    private readonly Action<TextFaultStage>? faultObserver;
    private readonly Action? postingBoundary;
    private readonly Action? deletionBoundary;
    private readonly Action? additionBoundary;
    private bool deletionObserved;
    private bool additionObserved;

    internal TextIncrementalOwner(string root, string leaf, Guid sourceNodeId,
        IOptions<TextExecutionOptions> executionOptions, Action<TextFaultStage>? faultObserver = null,
        TextResourceOwnership? resources = null)
    {
        this.root = root;
        this.resources = resources;
        this.leaf = leaf;
        this.sourceNodeId = sourceNodeId;
        this.executionOptions = executionOptions;
        this.faultObserver = faultObserver;
        postingBoundary = faultObserver is null ? null : ObservePosting;
        deletionBoundary = faultObserver is null ? null : ObserveDeletion;
        additionBoundary = faultObserver is null ? null : ObserveAddition;
    }

    internal TextResourceOwnership? Resources => resources;
    internal string Root => root;
    internal string Leaf => leaf;
    internal string Path => System.IO.Path.Combine(root, leaf);

    internal void Open(ReadExecutionBudget budget)
    {
        budget.Check();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (index is not null)
        {
            throw TextErrors.Ownership();
        }
        var owner = TextOwnerFiles.ReadOwner(System.IO.Path.Combine(Path, TextProtocol.OwnerFile),
            root, leaf, sourceNodeId, executionOptions);
        TextOwnedInventory.ValidateTrackedLayout(Path, owner.OwnedPaths, budget,
            runtimeAllowMissing: true, executionOptions: executionOptions);
        index = RuntimeTextIndex.Open(System.IO.Path.Combine(Path, TextProtocol.RuntimeDirectoryCore),
            new TextFileStreamProvider(root, leaf, sourceNodeId, executionOptions, resources), executionOptions);
        budget.Check();
    }

    internal void ApplyOnlineSeed(TextIncrementalSeedPlan seed, ReadExecutionBudget budget)
    {
        budget.Check();
        if (seed.Postings.Length != seed.Records.Length)
        {
            throw TextErrors.Corrupt();
        }
        var current = index ?? throw TextErrors.Ownership();
        foreach (var postings in seed.Postings)
        {
            budget.Check();
            TextIncrementalPostingWriter.Apply(current, [], postings, budget,
                postingObserved ? null : postingBoundary, null,
                additionObserved ? null : additionBoundary);
        }
        budget.Check();
    }

    internal void ApplyIntent(TextIncrementalIntent intent, long maximumIntentBytes, ReadExecutionBudget budget)
    {
        RequirePersistedIntent(intent, maximumIntentBytes, budget);
        var current = index ?? throw TextErrors.Ownership();
        foreach (var change in intent.Changes)
        {
            budget.Check();
            TextIncrementalPostingWriter.Apply(current, change.Removals, change.Additions, budget,
                postingObserved ? null : postingBoundary, deletionObserved ? null : deletionBoundary,
                additionObserved ? null : additionBoundary);
        }
        budget.Check();
    }

    internal TextFile[] CloseAndCapture(ReadExecutionBudget budget)
    {
        Close();
        budget.Check();
        var owner = TextOwnerFiles.ReadOwner(System.IO.Path.Combine(Path, TextProtocol.OwnerFile),
            root, leaf, sourceNodeId, executionOptions);
        var files = TextInventory.Capture(Path, owner.OwnedPaths, executionOptions, budget);
        TextFiles.CheckGenerationBound(Path, executionOptions, budget);
        budget.Check();
        return files;
    }

    private void RequirePersistedIntent(TextIncrementalIntent expected, long maximumBytes,
        ReadExecutionBudget budget)
    {
        var actual = TextIncrementalMetadata.ReadIntent(Path, maximumBytes, budget);
        budget.ChargeBytes(OrleansSerialization.Measure(expected));
        budget.ChargeBytes(OrleansSerialization.Measure(actual));
        var expectedBytes = OrleansSerialization.Serialize(expected);
        var actualBytes = OrleansSerialization.Serialize(actual);
        budget.Check();
        if (!expectedBytes.AsSpan().SequenceEqual(actualBytes))
        {
            throw TextErrors.Corrupt();
        }
    }

    internal Action<TextFaultStage>? FaultObserver => faultObserver;

    internal void Observe(TextFaultStage stage) => faultObserver?.Invoke(stage);

    private void ObservePosting()
    {
        if (postingObserved)
        {
            return;
        }
        postingObserved = true;
        Observe(TextFaultStage.PostingWritten);
    }

    private void ObserveDeletion()
    {
        if (deletionObserved)
        {
            return;
        }
        deletionObserved = true;
        Observe(TextFaultStage.DeletionWritten);
    }

    private void ObserveAddition()
    {
        if (additionObserved)
        {
            return;
        }
        additionObserved = true;
        Observe(TextFaultStage.AdditionWritten);
    }

    internal void CloseAfterOperation() => Close();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        Close();
        disposed = true;
    }

    private void Close()
    {
        if (index is not { } current)
        {
            return;
        }
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(current.EvictToDisk, failures);
        ServerFailureObserver.Observe(current.WaitForBackgroundThreads, failures);
        ServerFailureObserver.Observe(current.ZoneTree1.Maintenance.SaveMetaData, failures);
        var beforeDispose = failures.Count;
        try
        {
            index.Dispose();
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        if (failures.Count == beforeDispose)
        {
            index = null;
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
