using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using ZoneTree.FullTextSearch.Index;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextGeneration
{
    private readonly string root;
    private readonly Guid sourceNodeId;
    private List<NativeTextRecord>? pendingRecords = [];
    private NativeTextRecord[]? sealedRecords;
    private HashSet<EntityRef>? references = [];

    internal NativeTextGeneration(string root, string leaf, Guid sourceNodeId, TextProjectionScope scope,
        DatabaseLimits limits, NativeTextFileStreamProvider provider)
    {
        this.root = root;
        this.sourceNodeId = sourceNodeId;
        Leaf = leaf;
        Path = System.IO.Path.Combine(root, leaf);
        Scope = scope;
        Limits = limits;
        CurrentIndex = NativeTextIndex.Open(System.IO.Path.Combine(Path, NativeTextProtocol.NativeDirectory), provider);
    }

    internal string Leaf { get; }
    internal string Path { get; }
    internal TextProjectionScope Scope { get; }
    internal IndexOfTokenRecordPreviousToken<ulong, ulong>? CurrentIndex { get; private set; }
    internal IndexOfTokenRecordPreviousToken<ulong, ulong> Index
        => CurrentIndex ?? throw new ObjectDisposedException(nameof(NativeTextGeneration));
    internal DatabaseLimits Limits { get; }
    internal NativeTextFile[] Files { get; private set; } = [];
    internal Exception? DeferredBudgetFailure { get; private set; }
    internal IReadOnlyList<NativeTextRecord> Records => sealedRecords is { } records ? records : pendingRecords!;
    internal bool Published { get; private set; }

    internal void DeferBudgetFailure(Exception failure) => DeferredBudgetFailure ??= failure;

    internal void ClearDeferredBudgetFailure() => DeferredBudgetFailure = null;

    internal void AddRecord(EntityRef reference, long revision, ReadExecutionBudget budget)
    {
        if (pendingRecords!.Count >= Limits.MaxScanRecords)
        {
            throw NativeTextErrors.BoundExceeded();
        }
        if (revision < 0 || reference.Partition != Scope.Partition || reference.Collection != Scope.Collection
            || string.IsNullOrEmpty(reference.Id) || !references!.Add(reference))
        {
            throw NativeTextErrors.Corrupt();
        }
        var record = new NativeTextRecord((ulong)pendingRecords.Count + 1, reference, revision);
        budget.ChargeBytes(NativeSerialization.Measure(record));
        pendingRecords.Add(record);
    }

    internal NativeTextRecord[] SealRecords()
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

    internal NativeTextFile[] CloseAndCapture(ReadExecutionBudget? budget)
    {
        CloseIndex();
        var owner = NativeTextFiles.ReadOwnerForProvider(root, Leaf, sourceNodeId);
        try
        {
            Files = NativeTextInventory.Capture(Path, owner.OwnedPaths, budget);
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
        ServerFailureObserver.Observe(current.EvictToDisk, failures);
        var failuresBeforeDispose = failures.Count;
        ServerFailureObserver.Observe(current.Dispose, failures);
        if (failures.Count == failuresBeforeDispose)
        {
            CurrentIndex = null;
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void CaptureForSettlement(NativeTextOwnerReceipt owner, Exception primaryFailure)
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => Files = NativeTextInventory.Capture(Path, owner.OwnedPaths), failures);
        if (failures.Count > 0)
        {
            ClearDeferredBudgetFailure();
            throw new AggregateException(new[] { primaryFailure }.Concat(failures));
        }
    }

    internal void OpenIndex(ReadExecutionBudget budget)
    {
        if (CurrentIndex is not null)
        {
            return;
        }
        var manifest = NativeTextFiles.ValidatePublishedGeneration(root, Leaf, sourceNodeId, Scope, Limits, budget);
        VerifyRecords(manifest.Records, budget);
        VerifyFiles(manifest.Files, budget);
        budget.Check();
        CurrentIndex = NativeTextIndex.Open(System.IO.Path.Combine(Path, NativeTextProtocol.NativeDirectory),
            new NativeTextFileStreamProvider(root, Leaf, sourceNodeId));
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
    }

    private void VerifyRecords(NativeTextRecord[] actual, ReadExecutionBudget budget)
    {
        if (actual.Length != Records.Count)
        {
            throw NativeTextErrors.Corrupt();
        }
        for (var index = 0; index < actual.Length; index++)
        {
            budget.Check();
            if (actual[index] != Records[index])
            {
                throw NativeTextErrors.Corrupt();
            }
        }
    }

    private void VerifyFiles(NativeTextFile[] actual, ReadExecutionBudget budget)
    {
        if (actual.Length != Files.Length)
        {
            throw NativeTextErrors.Corrupt();
        }
        for (var index = 0; index < actual.Length; index++)
        {
            budget.Check();
            if (actual[index].RelativePath != Files[index].RelativePath || actual[index].Length != Files[index].Length
                || actual[index].Sha256 is null || Files[index].Sha256 is null
                || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    actual[index].Sha256, Files[index].Sha256))
            {
                throw NativeTextErrors.Corrupt();
            }
        }
    }
}
