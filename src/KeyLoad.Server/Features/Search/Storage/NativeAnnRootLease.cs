using KeyLoad.Storage.IO;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnRootLease : IDisposable
{
    private const int RootReceiptOwnershipFrames = 4;
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private readonly FileStream ownership;
    private readonly Lock receiptGate = new();
    private bool disposed;
    private bool uncertainOriginal;
    private NativeAnnRootReceipt receipt;

    private NativeAnnRootLease(string root, FileStream ownership, NativeAnnRootReceipt receipt)
    {
        Root = root;
        this.ownership = ownership;
        this.receipt = receipt;
    }

    internal static long ReceiptReservation(NativeAnnExecutionOptions options)
        => checked((long)RootReceiptOwnershipFrames * options.MaximumManifestBytes);

    internal string Root
    {
        get;
    }
    internal NativeAnnRootReceipt Receipt
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
            throw Errors.Fail(ErrorCode.RecoveryRequired, NativeAnnProtocol.Interrupted);
        }
    }

    internal static NativeAnnRootLease Open(string canonicalRoot, Guid nodeId, Guid incarnation,
        NativeAnnExecutionOptions options)
    {
        options.Validate();
        if (ReceiptReservation(options) >= options.MaximumResidentBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, NativeAnnProtocol.Bound);
        }
        NativeAnnPaths.RequireDirectory(canonicalRoot);
        var root = Path.GetFullPath(Path.Combine(canonicalRoot, NativeAnnProtocol.RootDirectory));
        if (!Directory.Exists(root))
        {
            Directory.CreateDirectory(root);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(root, PrivateDirectory);
            }
        }
        NativeAnnPaths.RequireDirectory(root);
        if (!File.Exists(Path.Combine(root, NativeAnnProtocol.RootReceipt))
            && !File.Exists(Path.Combine(root, NativeAnnProtocol.RootPending))
            && Directory.EnumerateFileSystemEntries(root).Any(path => Path.GetFileName(path) != NativeAnnProtocol.RootLock))
        {
            throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership);
        }
        var lockPath = Path.Combine(root, NativeAnnProtocol.RootLock);
        if (!File.Exists(lockPath))
        {
            NativeAnnReceiptFiles.Write(lockPath, NativeAnnProtocol.Version, options);
        }
        FileStream? file = null;
        Exception? failure = null;
        try
        {
            file = OfflineRegularFile.Open(lockPath, FileAccess.ReadWrite, FileShare.None, options.FileBufferBytes);
            NativeAnnLockFormat.Require(file, options);
            var receipt = NativeAnnRootReceipts.Open(root, nodeId, incarnation, options);
            var result = new NativeAnnRootLease(root, file, receipt);
            file = null;
            return result;
        }
        catch (Exception error) { failure = error; throw; }
        finally { NativeAnnUntransferred.Dispose(file, failure); }
    }

    internal void Enroll(string leaf, NativeAnnExecutionOptions options)
        => Update(current => NativeAnnRootReceipts.Enroll(Root, current, leaf, options));

    internal void Publish(NativeAnnOwnedKey key, NativeAnnPointer pointer, NativeAnnExecutionOptions options)
        => Update(current => NativeAnnPublication.Publish(Root, current, key, pointer, options));

    internal void Stage(NativeAnnOwnedKey key, NativeAnnPointer pointer, NativeAnnExecutionOptions options)
        => Update(current => NativeAnnPublication.Stage(Root, current, key, pointer, options));

    internal void Unpublish(NativeAnnOwnedKey key, NativeAnnExecutionOptions options, bool releaseOriginal = false)
    {
        lock (receiptGate)
        {
            SaveOriginal(NativeAnnPublication.PrepareUnpublish(Receipt, key, releaseOriginal), options);
        }
    }

    internal void Remove(string leaf, NativeAnnExecutionOptions options)
        => Update(current => NativeAnnPublication.Remove(Root, current, leaf, options));

    private void Update(Func<NativeAnnRootReceipt, NativeAnnRootReceipt> replace)
    {
        lock (receiptGate)
        {
            RequireStable();
            receipt = replace(receipt);
        }
    }

    internal AnnMaintenanceResult? AdmitOriginal(PrincipalRecord principal, AnnMaintenanceRequest request,
        ProjectionConsumerInfo configured, KeyLoad.Core.Features.Search.AnnSeed current, NativeAnnExecutionOptions options)
    {
        lock (receiptGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var admitted = NativeAnnOriginalOperations.Admit(Receipt, principal, request, configured, current, options);
            SaveOriginal(admitted.Receipt, options);
            return admitted.Result;
        }
    }

    internal void StartOriginal(AnnMaintenanceRequest request, NativeAnnExecutionOptions options)
    {
        lock (receiptGate)
        {
            SaveOriginal(NativeAnnOriginalOperations.Start(Receipt, request), options);
        }
    }

    internal void SettleOriginal(PrincipalRecord principal, AnnMaintenanceRequest request,
        AnnMaintenanceResult result, KeyLoad.Core.Features.Search.AnnSeed current, NativeAnnExecutionOptions options,
        CommitProjectionBatchRequest? settlementIntent)
    {
        lock (receiptGate)
        {
            SaveOriginal(NativeAnnOriginalOperations.Settle(Receipt, principal, request, result, current, settlementIntent, options), options);
        }
    }

    internal void CompleteOriginalRelease(NativeAnnOwnedKey key, NativeAnnExecutionOptions options)
    {
        lock (receiptGate)
        {
            SaveOriginal(NativeAnnOriginalOperations.CompleteRelease(Receipt, key), options);
        }
    }

    private void SaveOriginal(NativeAnnRootReceipt next, NativeAnnExecutionOptions options)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ReferenceEquals(Receipt, next))
        {
            return;
        }
        NativeAnnRootInventory.Require(Root, next, next.NodeId, next.Incarnation, options);
        if (global::KeyLoad.NativeSerialization.Measure(next) > options.MaximumManifestBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound);
        }
        try
        {
            NativeAnnRootReceipts.Replace(Root, next, options);
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
