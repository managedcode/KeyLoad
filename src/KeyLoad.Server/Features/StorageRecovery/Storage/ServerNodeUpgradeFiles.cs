using Microsoft.Extensions.Options;
namespace KeyLoad.Server;

internal static class ServerNodeUpgradeFiles
{
    internal static void RequireRegularFile(string path)
    {
        ServerNodeUpgradePaths.CheckAncestors(path, false);
        KeyLoad.Storage.IO.OfflineRegularFile.RequireRegular(path);
    }

    internal static void CreatePrivateDirectory(string path)
    {
        ServerNodeUpgradePaths.CheckAncestors(path, true);
        if (File.Exists(path) || Directory.Exists(path))
        { throw Errors.Fail(ErrorCode.Conflict, ServerNodeUpgradeProtocol.Invalid); }
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    internal static FileStream CreatePrivateFile(string path, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = executionOptions.Value.FileBufferBytes,
            Options = FileOptions.WriteThrough
        };
        if (!OperatingSystem.IsWindows())
        { options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; }
        return new(path, options);
    }

    internal static void Copy(string source, string destination, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
    {
        RequireRegularFile(source);
        using var input = OpenRead(source, executionOptions: executionOptions);
        using var output = CreatePrivateFile(destination, executionOptions: executionOptions);
        input.CopyTo(output, executionOptions.Value.FileBufferBytes);
        output.Flush(true);
    }

    internal static void CreateEmpty(string path, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
    {
        using var file = CreatePrivateFile(path, executionOptions: executionOptions);
        file.Flush(true);
    }

    internal static FileStream OpenRead(string path, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
    {
        ServerNodeUpgradePaths.CheckAncestors(path, false);
        return KeyLoad.Storage.IO.OfflineRegularFile.Open(path, FileAccess.Read, FileShare.Read,
            executionOptions.Value.FileBufferBytes);
    }
}
