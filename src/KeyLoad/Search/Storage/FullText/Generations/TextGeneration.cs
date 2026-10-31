using KeyLoad.Core;
using KeyLoad.Query.Search;
using Microsoft.Extensions.Options;
using ZoneTree.FullTextSearch.Index;

namespace KeyLoad.Server.Search;

internal sealed class TextGeneration
{
    private readonly IOptions<TextExecutionOptions> executionOptions;

    private readonly string root;
    private readonly Guid sourceNodeId;
    private readonly TextResourceOwnership? resources;
    internal TextResourceOwnership? Resources => resources;
    private List<TextRecord>? pendingRecords = [];
    private TextRecord[]? sealedRecords;
    private TextFile[]? openedFiles;
    private HashSet<EntityRef>? references = [];

    internal TextGeneration(string root, string leaf, Guid sourceNodeId, TextProjectionScope scope, TextFileStreamProvider provider, IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership? resources = null)
    {
        this.executionOptions = executionOptions;
        this.resources = resources;
        this.root = root;
        this.sourceNodeId = sourceNodeId;
        Leaf = leaf;
        Path = System.IO.Path.Combine(root, leaf);
        Scope = scope;
        CurrentIndex = RuntimeTextIndex.Open(System.IO.Path.Combine(Path, TextProtocol.RuntimeDirectoryCore), provider, executionOptions: executionOptions);
    }

    internal string Leaf
    {
        get;
    }
    internal string Path
    {
        get;
    }
    internal TextProjectionScope Scope
    {
        get;
    }
    internal IndexOfTokenRecordPreviousToken<ulong, ulong>? CurrentIndex
    {
        get; private set;
    }
    internal IndexOfTokenRecordPreviousToken<ulong, ulong> Index
        => CurrentIndex ?? throw new ObjectDisposedException(nameof(TextGeneration));
    internal TextFile[] Files { get; private set; } = [];
    internal Exception? DeferredBudgetFailure
    {
        get; private set;
    }
    internal IReadOnlyList<TextRecord> Records => sealedRecords is { } records ? records : pendingRecords!;
    internal bool Published
    {
        get; private set;
    }

    internal void DeferBudgetFailure(Exception failure) => DeferredBudgetFailure ??= failure;

    internal void ClearDeferredBudgetFailure() => DeferredBudgetFailure = null;

    internal void AddRecord(EntityRef reference, long revision, ReadExecutionBudget budget)
    {
        const int RevisionValidationBoundary = 0;
        const int PendingRecordsCountStep = 1;

        if (revision < RevisionValidationBoundary || reference.Partition != Scope.Partition || reference.Collection != Scope.Collection
            || string.IsNullOrEmpty(reference.Id) || !references!.Add(reference))
        {
            throw TextErrors.Corrupt();
        }
        var record = new TextRecord((ulong)pendingRecords!.Count + PendingRecordsCountStep, reference, revision);
        budget.ChargeBytes(OrleansSerialization.Measure(record));
        pendingRecords.Add(record);
    }

    internal TextRecord[] SealRecords()
    {
        if (sealedRecords is not null)
        {
            return sealedRecords;
        }
        sealedRecords = pendingRecords!.ToArray();
        pendingRecords = null;
        references = null;
        return sealedRecords;
    }

    internal TextFile[] CloseAndCapture(ReadExecutionBudget? budget)
    {
        CloseIndex();
        var owner = TextFiles.ReadOwnerForProvider(root, Leaf, sourceNodeId, executionOptions: executionOptions);
        try
        {
            Files = TextInventory.Capture(Path, owner.OwnedPaths, budget: budget, executionOptions: executionOptions);
        }
        catch (OperationCanceledException error)
        {
            DeferBudgetFailure(error);
            CaptureForSettlement(owner, error);
        }
        catch (KeyLoadException error) when (error.Code == KeyLoad.ErrorCode.BudgetExceeded)
        {
            DeferBudgetFailure(error);
            CaptureForSettlement(owner, error);
        }
        return Files;
    }

    private void CloseIndex()
    {
        if (CurrentIndex is not { } current)
        {
            return;
        }
        var failures = new List<Exception>();
        if (!current.IsReadOnly)
        {
            ServerFailureObserver.Observe(current.EvictToDisk, failures);
        }
        var failuresBeforeDispose = failures.Count;
        ServerFailureObserver.Observe(current.Dispose, failures);
        if (failures.Count == failuresBeforeDispose)
        {
            CurrentIndex = null;
            openedFiles = null;
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void CaptureForSettlement(TextOwnerReceipt owner, Exception primaryFailure)
    {
        const int FailuresCountValidationBoundary = 0;

        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => Files = TextInventory.Capture(Path, owner.OwnedPaths, executionOptions: executionOptions), failures);
        if (failures.Count > FailuresCountValidationBoundary)
        {
            ClearDeferredBudgetFailure();
            throw new AggregateException(new[] { primaryFailure }.Concat(failures));
        }
    }

    internal void OpenIndex(ReadExecutionBudget budget)
    {
        budget.Check();
        if (CurrentIndex is not null)
        {
            TextLiveGenerationMetadata.Validate(this, Path, root, Leaf, sourceNodeId, executionOptions);
            var liveOwner = TextFiles.ReadOwnerForProvider(root, Leaf, sourceNodeId, executionOptions);
            TextInventory.Verify(Path, liveOwner.OwnedPaths, openedFiles ?? throw TextErrors.Corrupt(),
                executionOptions, budget);
            return;
        }
        var manifest = TextFiles.ValidatePublishedGeneration(root, Leaf, sourceNodeId, Scope, budget, executionOptions: executionOptions);
        if (!TextLiveManifest.Matches(this, manifest, budget))
        {
            throw TextErrors.Corrupt();
        }
        budget.Check();
        CurrentIndex = RuntimeTextIndex.Open(System.IO.Path.Combine(Path, TextProtocol.RuntimeDirectoryCore),
            new TextFileStreamProvider(root, Leaf, sourceNodeId, executionOptions: executionOptions,
                resources: resources, allowReadOnlyInventory: true), executionOptions: executionOptions);
        CurrentIndex.IsReadOnly = true;
        CurrentIndex.WaitForBackgroundThreads();
        var owner = TextFiles.ReadOwnerForProvider(root, Leaf, sourceNodeId, executionOptions);
        openedFiles = TextInventory.Capture(Path, owner.OwnedPaths, executionOptions, budget);
    }

    internal void MarkPublished()
    {
        references = null;
        Published = true;
    }

    internal void DisposeIndex()
    {
        var current = CurrentIndex;
        if (current is null)
        {
            return;
        }
        current.Dispose();
        CurrentIndex = null;
        openedFiles = null;
    }

}
