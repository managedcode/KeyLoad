using KeyLoad.Storage.ZoneTree.IO;

namespace KeyLoad.Server.Features.Search;

internal sealed class AnnRootLease : IDisposable
{
    private const int RootReceiptOwnershipFrames = 4;
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private readonly FileStream ownership;
    private readonly Lock receiptGate = new();
    private bool disposed;
    private bool uncertainOriginal;
    private AnnRootReceipt receipt;

    private AnnRootLease(string root, FileStream ownership, AnnRootReceipt receipt)
    {
        Root = root;
        this.ownership = ownership;
        this.receipt = receipt;
    }

    internal static long ReceiptReservation(AnnExecutionOptions options)
        => checked((long)RootReceiptOwnershipFrames * options.MaximumManifestBytes);

    internal string Root
    {
        get;
    }
    internal AnnRootReceipt Receipt
    {
        get
        {
            lock (receiptGate)
            {
                RequireStable();
                return receipt;
            }
        }
        private set
        {
            receipt = value;
        }
    }

    private void RequireStable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (uncertainOriginal)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, AnnProtocol.Interrupted);
        }
    }

    internal static AnnRootLease Open(string canonicalRoot, Guid nodeId, Guid incarnation,
        AnnExecutionOptions options)
    {
        options.Validate();
        if (ReceiptReservation(options) >= options.MaximumResidentBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, AnnProtocol.Bound);
        }
        AnnPaths.RequireDirectory(canonicalRoot);
        var root = Path.GetFullPath(Path.Combine(canonicalRoot, AnnProtocol.RootDirectory));
        if (!Directory.Exists(root))
        {
            Directory.CreateDirectory(root);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(root, PrivateDirectory);
            }
        }
        AnnPaths.RequireDirectory(root);
        if (!File.Exists(Path.Combine(root, AnnProtocol.RootReceipt))
            && !File.Exists(Path.Combine(root, AnnProtocol.RootPending))
            && Directory.EnumerateFileSystemEntries(root).Any(path => Path.GetFileName(path) != AnnProtocol.RootLock))
        {
            throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Ownership);
        }
        var lockPath = Path.Combine(root, AnnProtocol.RootLock);
        if (!File.Exists(lockPath))
        {
            AnnReceiptFiles.Write(lockPath, AnnProtocol.Version, options);
        }
        FileStream? file = null;
        Exception? failure = null;
        try
        {
            file = OfflineRegularFile.Open(lockPath, FileAccess.ReadWrite, FileShare.None, options.FileBufferBytes);
            AnnLockFormat.Require(file, options);
            var receipt = AnnRootReceipts.Open(root, nodeId, incarnation, options);
            var result = new AnnRootLease(root, file, receipt);
            file = null;
            return result;
        }
        catch (Exception error) { failure = error; throw; }
        finally { AnnUntransferred.Dispose(file, failure); }
    }

    internal void Enroll(string leaf, AnnExecutionOptions options)
        => Update(current => AnnRootReceipts.Enroll(Root, current, leaf, options));

    internal void Publish(AnnOwnedKey key, AnnPointer pointer, AnnExecutionOptions options)
        => Update(current => AnnPublication.Publish(Root, current, key, pointer, options));

    internal void Stage(AnnOwnedKey key, AnnPointer pointer, AnnExecutionOptions options)
        => Update(current => AnnPublication.Stage(Root, current, key, pointer, options));

    internal void Unpublish(AnnOwnedKey key, AnnExecutionOptions options, bool releaseOriginal = false)
    {
        lock (receiptGate)
        {
            SaveOriginal(AnnPublication.PrepareUnpublish(Receipt, key, releaseOriginal), options);
        }
    }

    internal void Remove(string leaf, AnnExecutionOptions options)
        => Update(current => AnnPublication.Remove(Root, current, leaf, options));

    private void Update(Func<AnnRootReceipt, AnnRootReceipt> replace)
    {
        lock (receiptGate)
        {
            RequireStable();
            receipt = replace(receipt);
        }
    }

    internal AnnMaintenanceResult? AdmitOriginal(PrincipalRecord principal, AnnMaintenanceRequest request,
        ProjectionConsumerInfo configured, KeyLoad.Core.Features.Search.AnnSeed current, AnnExecutionOptions options)
    {
        lock (receiptGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var admitted = AnnOriginalOperations.Admit(Receipt, principal, request, configured, current, options);
            SaveOriginal(admitted.Receipt, options);
            return admitted.Result;
        }
    }

    internal void StartOriginal(AnnMaintenanceRequest request, AnnExecutionOptions options)
    {
        lock (receiptGate)
        {
            SaveOriginal(AnnOriginalOperations.Start(Receipt, request), options);
        }
    }

    internal void SettleOriginal(PrincipalRecord principal, AnnMaintenanceRequest request,
        AnnMaintenanceResult result, KeyLoad.Core.Features.Search.AnnSeed current, AnnExecutionOptions options,
        CommitProjectionBatchRequest? settlementIntent)
    {
        lock (receiptGate)
        {
            SaveOriginal(AnnOriginalOperations.Settle(Receipt, principal, request, result, current, settlementIntent, options), options);
        }
    }

    internal void CompleteOriginalRelease(AnnOwnedKey key, AnnExecutionOptions options)
    {
        lock (receiptGate)
        {
            SaveOriginal(AnnOriginalOperations.CompleteRelease(Receipt, key), options);
        }
    }

    private void SaveOriginal(AnnRootReceipt next, AnnExecutionOptions options)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ReferenceEquals(Receipt, next))
        {
            return;
        }
        AnnRootInventory.Require(Root, next, next.NodeId, next.Incarnation, options);
        if (global::KeyLoad.OrleansSerialization.Measure(next) > options.MaximumManifestBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnProtocol.Bound);
        }
        try
        {
            AnnRootReceipts.Replace(Root, next, options);
        }
        catch (Exception) { uncertainOriginal = true; throw; }
        Receipt = next;
    }

    public void Dispose()
    {
        lock (receiptGate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            ownership.Dispose();
        }
    }
}
