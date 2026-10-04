namespace KeyLoad.Server;

internal static class ServerNodeUpgradePrivateDirectory
{
    private const string DirectoryPrefix = ".node-upgrade-verify-";
    private const string GuidFormat = "N";

    internal static T Run<T>(string parent, Func<string, T> action)
    {
        var directory = Path.Combine(parent, DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        ServerNodeUpgradeFiles.CreatePrivateDirectory(directory);
        T result;
        try
        {
            result = action(directory);
        }
        catch (Exception error)
        {
            try
            { Directory.Delete(directory, recursive: true); }
            catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
            throw;
        }
        Directory.Delete(directory, recursive: true);
        return result;
    }
}
