using Microsoft.Extensions.Options;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeStoreOpeningOptions
{
    internal static ZoneTreeStoreOptions Freeze(ZoneTreeStoreOptions options,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Directory);
        ArgumentNullException.ThrowIfNull(executionOptions);
        var executionPolicy = executionOptions.Value;
        ArgumentNullException.ThrowIfNull(executionPolicy);
        executionPolicy.Validate();
        IOptions<ZoneTreeStorageExecutionOptions> frozenExecution = new OptionsWrapper<ZoneTreeStorageExecutionOptions>(executionPolicy);
        return options.ResolveExecutionOptions(frozenExecution);
    }
}
