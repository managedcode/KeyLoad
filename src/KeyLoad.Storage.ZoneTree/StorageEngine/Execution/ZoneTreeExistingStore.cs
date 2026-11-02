using Microsoft.Extensions.Options;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Opens only original current-format stores inside the separately owned inspector process.</summary>
internal static class ZoneTreeExistingStore
{
    private const string InvalidScope = "Original store inspection requires a captured node identity and incarnation.";
    private const string InvalidDirectory = "Original store inspection requires an absolute existing canonical directory.";
    private const string InvalidOptions = "Original store inspection does not permit cache or commit observers.";

    internal static ZoneTreeStore Open(ZoneTreeStoreOptions options, Guid expectedNodeId,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(options) /* KLD0041: BCL null validation accepts the options ZoneTreeStoreOptions reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(executionOptions) /* KLD0041: BCL null validation accepts the executionOptions IOptions reference through its object parameter. */;
        var policy = executionOptions.Value;
        ArgumentNullException.ThrowIfNull(policy) /* KLD0041: BCL null validation accepts the policy ZoneTreeStorageExecutionOptions reference through its object parameter. */;
        policy.Validate();
        options = options.WithExecutionSnapshot(policy);
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

    internal static void Validate(ZoneTreeStoreOptions options, Guid expectedNodeId)
    {
        ArgumentNullException.ThrowIfNull(options) /* KLD0041: BCL null validation accepts the options ZoneTreeStoreOptions reference through its object parameter. */;
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
        if (options.FaultObserver is not null)
        {
            throw new ArgumentException(InvalidOptions, nameof(options));
        }
    }
}
