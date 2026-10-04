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

    internal static FileStream CreatePrivateFile(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = ServerNodeUpgradeProtocol.BufferBytes,
            Options = FileOptions.WriteThrough
        };
        if (!OperatingSystem.IsWindows())
        { options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite; }
        return new(path, options);
    }

    internal static void Copy(string source, string destination)
    {
        RequireRegularFile(source);
        using var input = OpenRead(source);
        using var output = CreatePrivateFile(destination);
        input.CopyTo(output, ServerNodeUpgradeProtocol.BufferBytes);
        output.Flush(true);
    }

    internal static void CreateEmpty(string path)
    {
        using var file = CreatePrivateFile(path);
        file.Flush(true);
    }

    internal static FileStream OpenRead(string path)
    {
        ServerNodeUpgradePaths.CheckAncestors(path, false);
        return KeyLoad.Storage.IO.OfflineRegularFile.Open(path, FileAccess.Read, FileShare.Read,
            ServerNodeUpgradeProtocol.BufferBytes);
    }
}
