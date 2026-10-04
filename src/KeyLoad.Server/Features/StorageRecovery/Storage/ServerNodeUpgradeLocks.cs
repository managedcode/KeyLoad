namespace KeyLoad.Server;

internal sealed class ServerNodeUpgradeLocks : IDisposable
{
    private readonly List<FileStream> files = [];
    internal Dictionary<string, FileStream> Held { get; } = new(StringComparer.Ordinal);

    internal static ServerNodeUpgradeLocks Acquire(string directory)
    {
        var result = new ServerNodeUpgradeLocks();
        try
        {
            result.Add(directory, ServerNodeUpgradeProtocol.NodeOwner);
            result.Add(directory, Path.Combine(ServerNodeUpgradeProtocol.Canonical, ServerNodeUpgradeProtocol.StoreOwner));
            result.Add(directory, Path.Combine(ServerNodeUpgradeProtocol.Replica, ServerNodeUpgradeProtocol.StoreOwner));
            return result;
        }
        catch (Exception error)
        {
            try
            { result.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
            throw;
        }
    }

    private void Add(string directory, string relative)
    {
        var path = Path.Combine(directory, relative);
        ServerNodeUpgradeFiles.RequireRegularFile(path);
        var file = KeyLoad.Storage.IO.OfflineRegularFile.Open(path, FileAccess.ReadWrite, FileShare.None,
            ServerNodeUpgradeProtocol.BufferBytes);
        files.Add(file);
        if (file.Length != 0)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
        Held.Add(relative.Replace(Path.DirectorySeparatorChar, '/'), file);
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        for (var index = files.Count - 1; index >= 0; index--)
        {
            ServerFailureObserver.Observe(files[index].Dispose, failures);
        }
        files.Clear();
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
