using Microsoft.Extensions.Options;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeStoreOpeningOptions
{
    internal static ZoneTreeStoreOptions Freeze(ZoneTreeStoreOptions options,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(options) /* KLD0041: BCL null validation accepts the options ZoneTreeStoreOptions reference through its object parameter. */;
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Directory);
        ArgumentNullException.ThrowIfNull(executionOptions) /* KLD0041: BCL null validation accepts the executionOptions IOptions reference through its object parameter. */;
        var executionPolicy = executionOptions.Value;
        ArgumentNullException.ThrowIfNull(executionPolicy) /* KLD0041: BCL null validation accepts the executionPolicy ZoneTreeStorageExecutionOptions reference through its object parameter. */;
        executionPolicy.Validate();
        IOptions<ZoneTreeStorageExecutionOptions> frozenExecution = new OptionsWrapper<ZoneTreeStorageExecutionOptions>(executionPolicy);
        return options.ResolveExecutionOptions(frozenExecution);
    }
}
