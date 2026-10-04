namespace KeyLoad.Storage.ZoneTree;

/// <summary>Opens only original current-format stores inside the separately owned inspector process.</summary>
internal static class ZoneTreeExistingStore
{
    private const string InvalidScope = "Original store inspection requires a captured node identity and incarnation.";
    private const string InvalidDirectory = "Original store inspection requires an absolute existing canonical directory.";
    private const string InvalidOptions = "Original store inspection does not permit cache or commit observers.";

    internal static ZoneTreeStore Open(ZoneTreeStoreOptions options, Guid expectedNodeId)
    {
        Validate(options, expectedNodeId);
        var runtime = new ZoneTreeStoreRuntime(options, expectedNodeId);
        try
        {
            return new(runtime, expectedNodeId);
        }
        catch (Exception failure)
        {
            ZoneTreeExistingStoreCleanup.FailedHandoff(runtime, failure);
            throw;
        }
    }

    private static void Validate(ZoneTreeStoreOptions options, Guid expectedNodeId)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Directory);
        if (!Path.IsPathFullyQualified(options.Directory)
            || !string.Equals(Path.GetFullPath(options.Directory), options.Directory, StringComparison.Ordinal))
        {
            throw new ArgumentException(InvalidDirectory, nameof(options));
        }
        if (!Directory.Exists(options.Directory))
        {
            throw new DirectoryNotFoundException(InvalidDirectory);
        }
        if (expectedNodeId == Guid.Empty || options.Incarnation is null || options.Incarnation == Guid.Empty)
        {
            throw new ArgumentException(InvalidScope, nameof(options));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxFrameBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxSnapshotBytes);
        if (options.EmbeddedPointCache is not null || options.FaultObserver is not null)
        {
            throw new ArgumentException(InvalidOptions, nameof(options));
        }
    }
}
