using Microsoft.Extensions.Options;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeIdentityPreflight
{
    internal static void Validate(ZoneTreeStoreOptions options,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(options) /* KLD0041: BCL null validation accepts the options ZoneTreeStoreOptions reference through its object parameter. */;
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Directory);
        ArgumentNullException.ThrowIfNull(executionOptions) /* KLD0041: BCL null validation accepts the executionOptions IOptions reference through its object parameter. */;
        ZoneTreeIdentityFile.ValidateBeforeOpen(options.ResolveExecutionOptions(executionOptions));
    }
}
