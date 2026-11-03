using KeyLoad.Core;
using KeyLoad.Query.Search;
using Microsoft.Extensions.Options;
using ZoneTree.FullTextSearch.Index;
using ZoneTree.Options;

namespace KeyLoad.Server.Search;

internal sealed class TextGeneration
{
    private const string UnpersistedPostings = "The native text index retained unpersisted postings after joined eviction.";
    private readonly IOptions<TextExecutionOptions> executionOptions;

    private readonly string root;
    private readonly Guid sourceNodeId;
    private readonly TextResourceOwnership resources;
    internal TextResourceOwnership Resources => resources;

    internal TextGeneration(string root, string leaf, Guid sourceNodeId, TextProjectionScope scope, TextFileStreamProvider provider, IOptions<TextExecutionOptions> executionOptions, TextResourceOwnership resources)
    {
        this.executionOptions = executionOptions;
        this.resources = resources;
        this.root = root;
        this.sourceNodeId = sourceNodeId;
        Leaf = leaf;
        Path = System.IO.Path.Combine(root, leaf);
        Scope = scope;
        Corpus = new(scope);
        CurrentIndex = RuntimeTextIndex.Open(System.IO.Path.Combine(Path, TextProtocol.RuntimeDirectoryCore), provider, executionOptions: executionOptions, walMode: WriteAheadLogMode.None);
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
    internal TextGenerationCorpus Corpus { get; }
    internal bool Published
    {
        get; private set;
    }
    internal bool IsSealedRead => Published && CurrentIndex is not { IsReadOnly: false };

    internal void DeferBudgetFailure(Exception failure) => DeferredBudgetFailure ??= failure;

    internal void ClearDeferredBudgetFailure() => DeferredBudgetFailure = null;

    internal TextFile[] CloseAndCapture(ReadExecutionBudget? budget)
        => CloseAndCapture(budget, certificateBudget: null);

    internal TextFile[] SealAndCapture(ReadExecutionBudget budget)
        => CloseAndCapture(budget, certificateBudget: budget);

    private TextFile[] CloseAndCapture(ReadExecutionBudget? budget, ReadExecutionBudget? certificateBudget)
    {
        var sealedRead = IsSealedRead;
        CloseIndex(certificateBudget);
        var owner = TextFiles.ReadOwnerForProvider(root, Leaf, sourceNodeId, executionOptions: executionOptions);
        TextFile[] captured = [];
        try
        {
            captured = TextInventory.Capture(Path, owner.OwnedPaths, budget: budget, executionOptions: executionOptions);
        }
        catch (OperationCanceledException error)
        {
            DeferBudgetFailure(error);
            CompleteForSettlement(error, () => captured = TextInventory.Capture(Path, owner.OwnedPaths, executionOptions: executionOptions));
        }
        catch (KeyLoadException error) when (error.Code == KeyLoad.ErrorCode.BudgetExceeded)
        {
            DeferBudgetFailure(error);
            CompleteForSettlement(error, () => captured = TextInventory.Capture(Path, owner.OwnedPaths, executionOptions: executionOptions));
        }
        if (!sealedRead)
        {
            Files = captured;
        }
        return captured;
    }

    internal void VerifyPublished(ReadExecutionBudget? budget)
    {
        try
        {
            RequirePublished(budget);
        }
        catch (Exception error) when (budget is not null && error is (OperationCanceledException
            or KeyLoadException { Code: KeyLoad.ErrorCode.BudgetExceeded }))
        {
            DeferBudgetFailure(error);
            CompleteForSettlement(error, () => RequirePublished(null));
        }
    }

    private void RequirePublished(ReadExecutionBudget? budget)
    {
        var manifest = TextFiles.ValidatePublishedGeneration(root, Leaf, sourceNodeId, Scope, budget, executionOptions);
        if (!TextLiveManifest.Matches(this, manifest, budget))
        {
            throw TextErrors.Corrupt();
        }
    }

    private void CloseIndex(ReadExecutionBudget? certificateBudget)
    {
        if (CurrentIndex is not { } current)
        {
            return;
        }
        var failures = new List<Exception>();
        if (!current.IsReadOnly)
        {
            ServerFailureObserver.Observe(current.EvictToDisk, failures);
            ServerFailureObserver.Observe(() =>
            {
                current.WaitForBackgroundThreads();
                if (current.ZoneTree1.Maintenance.InMemoryRecordCount != 0)
                {
                    throw KeyLoad.Errors.Fail(KeyLoad.ErrorCode.Corruption, UnpersistedPostings);
                }
            }, failures);
        }
        if (certificateBudget is not null && failures.Count == 0)
        {
            ServerFailureObserver.Observe(() => Corpus.Seal(current.ZoneTree1,
                executionOptions.Value.MaximumDiskBytes, certificateBudget), failures);
        }
        var failuresBeforeDispose = failures.Count;
        ServerFailureObserver.Observe(current.Dispose, failures);
        if (failures.Count == failuresBeforeDispose)
        {
            CurrentIndex = null;
            Corpus.ReleasePostingBuilder();
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void CompleteForSettlement(Exception primaryFailure, Action complete)
    {
        const int FailuresCountValidationBoundary = 0;

        var failures = new List<Exception>();
        ServerFailureObserver.Observe(complete, failures);
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
            return;
        }
        RequirePublished(budget);
        budget.Check();
        CurrentIndex = RuntimeTextIndex.Open(System.IO.Path.Combine(Path, TextProtocol.RuntimeDirectoryCore),
            new TextFileStreamProvider(root, Leaf, sourceNodeId, executionOptions: executionOptions,
                resources: resources, allowReadOnlyInventory: true), executionOptions: executionOptions, walMode: WriteAheadLogMode.None);
        CurrentIndex.IsReadOnly = true;
        CurrentIndex.WaitForBackgroundThreads();
    }

    internal void MarkPublished()
    {
        _ = Corpus.PostingProof;
        _ = Corpus.RecordDigest;
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
        Corpus.ReleasePostingBuilder();
    }
}
