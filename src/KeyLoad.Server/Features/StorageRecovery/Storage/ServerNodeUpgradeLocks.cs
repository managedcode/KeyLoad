using Microsoft.Extensions.Options;
namespace KeyLoad.Server;

internal sealed class ServerNodeUpgradeLocks : IDisposable
{
    private readonly List<FileStream> files = [];
    internal Dictionary<string, FileStream> Held { get; } = new(StringComparer.Ordinal);

    internal static ServerNodeUpgradeLocks Acquire(string directory, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
    {
        var result = new ServerNodeUpgradeLocks();
        try
        {
            result.Add(directory, ServerNodeUpgradeProtocol.NodeOwner, executionOptions: executionOptions);
            result.Add(directory, Path.Combine(ServerNodeUpgradeProtocol.Canonical, ServerNodeUpgradeProtocol.StoreOwner), executionOptions: executionOptions);
            result.Add(directory, Path.Combine(ServerNodeUpgradeProtocol.Replica, ServerNodeUpgradeProtocol.StoreOwner), executionOptions: executionOptions);
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

    private void Add(string directory, string relative, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
    {
        const int EmptyFileLength = 0;
        const char SlashCharacter = '/';

        var path = Path.Combine(directory, relative);
        ServerNodeUpgradeFiles.RequireRegularFile(path);
        var file = KeyLoad.Storage.IO.OfflineRegularFile.Open(path, FileAccess.ReadWrite, FileShare.None,
            executionOptions.Value.FileBufferBytes);
        files.Add(file);
        if (file.Length != EmptyFileLength)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
        Held.Add(relative.Replace(Path.DirectorySeparatorChar, SlashCharacter), file);
    }

    public void Dispose()
    {
        const int FilesCountStep = 1;
        const int IndexValidationBoundary = 0;

        var failures = new List<Exception>();
        for (var index = files.Count - FilesCountStep; index >= IndexValidationBoundary; index--)
        {
            ServerFailureObserver.Observe(files[index].Dispose, failures);
        }
        files.Clear();
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
