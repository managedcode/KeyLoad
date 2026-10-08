using KeyLoad.Storage.IO;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnRootLease : IDisposable
{
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private readonly FileStream ownership;
    private bool disposed;

    private NativeAnnRootLease(string root, FileStream ownership, NativeAnnRootReceipt receipt)
    { Root = root; this.ownership = ownership; Receipt = receipt; }

    internal string Root { get; }
    internal NativeAnnRootReceipt Receipt { get; private set; }

    internal static NativeAnnRootLease Open(string canonicalRoot, Guid nodeId, Guid incarnation,
        NativeAnnExecutionOptions options)
    {
        options.Validate();
        NativeAnnPaths.RequireDirectory(canonicalRoot);
        var root = Path.GetFullPath(Path.Combine(canonicalRoot, NativeAnnProtocol.RootDirectory));
        if (!Directory.Exists(root))
        {
            Directory.CreateDirectory(root);
            if (!OperatingSystem.IsWindows())
            { File.SetUnixFileMode(root, PrivateDirectory); }
        }
        NativeAnnPaths.RequireDirectory(root);
        if (!File.Exists(Path.Combine(root, NativeAnnProtocol.RootReceipt))
            && !File.Exists(Path.Combine(root, NativeAnnProtocol.RootPending))
            && Directory.EnumerateFileSystemEntries(root).Any(path => Path.GetFileName(path) != NativeAnnProtocol.RootLock))
        { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
        var lockPath = Path.Combine(root, NativeAnnProtocol.RootLock);
        if (!File.Exists(lockPath))
        { NativeAnnReceiptFiles.Write(lockPath, NativeAnnProtocol.Version, options); }
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
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Receipt = NativeAnnRootReceipts.Enroll(Root, Receipt, leaf, options);
    }

    internal void Publish(NativeAnnOwnedKey key, NativeAnnPointer pointer, NativeAnnExecutionOptions options)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Receipt = NativeAnnPublication.Publish(Root, Receipt, key, pointer, options);
    }

    internal void Stage(NativeAnnOwnedKey key, NativeAnnPointer pointer, NativeAnnExecutionOptions options)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Receipt = NativeAnnPublication.Stage(Root, Receipt, key, pointer, options);
    }

    internal void Unpublish(NativeAnnOwnedKey key, NativeAnnExecutionOptions options)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Receipt = NativeAnnPublication.Unpublish(Root, Receipt, key, options);
    }

    internal void Remove(string leaf, NativeAnnExecutionOptions options)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Receipt = NativeAnnPublication.Remove(Root, Receipt, leaf, options);
    }

    public void Dispose()
    {
        if (disposed)
        { return; }
        disposed = true;
        ownership.Dispose();
    }
}
