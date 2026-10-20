namespace KeyLoad.Server;

[ConfigurationOptions]
internal sealed record FileStorageOptions
{
    internal const string SectionName = "KeyLoad:FileStorage";
    private const int DefaultTransfers = 16;
    private const int DefaultSessions = 100;
    private const int CleanupSeconds = 5;
    internal const string SharedRootRequired = "Cluster file storage requires an explicitly configured shared physical directory.";
    internal const string InvalidSettings = "File storage settings are invalid.";
    private const int TimeoutMinutes = 10;
    private const int EmptyCount = 0;
    private const int SingleSilo = 1;
    private const string FilesDirectory = "files";
    public string RootDirectory { get; init; } = string.Empty;
    public bool SharedDirectory { get; init; }
    public int MaximumTransfers { get; init; } = DefaultTransfers;
    public int MaximumChunkSessions { get; init; } = DefaultSessions;
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(CleanupSeconds);
    public TimeSpan TransferTimeout { get; init; } = TimeSpan.FromMinutes(TimeoutMinutes);

    internal bool IsValid() => RootDirectory is not null && MaximumTransfers > EmptyCount
        && MaximumChunkSessions > EmptyCount && TransferTimeout > TimeSpan.Zero && CleanupTimeout > TimeSpan.Zero;

    internal string ResolveRoot(NodeOptions node)
    {
        if (node.SiloCount > SingleSilo && (!SharedDirectory || string.IsNullOrWhiteSpace(RootDirectory)))
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, SharedRootRequired);
        }
        return Path.GetFullPath(RootDirectory.Length == EmptyCount ? Path.Combine(node.DataDirectory, FilesDirectory) : RootDirectory);
    }
}
