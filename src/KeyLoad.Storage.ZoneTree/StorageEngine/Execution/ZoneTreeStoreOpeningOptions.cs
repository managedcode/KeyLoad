using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeStoreOpeningOptions
{
    internal static (ZoneTreeStoreOptions Options, IOptions<ZoneTreeStorageExecutionOptions> Execution,
        IOptions<ZoneTreePointCacheExecutionOptions> Cache) Freeze(ZoneTreeStoreOptions options,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions,
        IOptions<ZoneTreePointCacheExecutionOptions> cacheExecutionOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Directory);
        options.EmbeddedPointCache?.Validate();
        ArgumentNullException.ThrowIfNull(executionOptions);
        var executionPolicy = executionOptions.Value;
        ArgumentNullException.ThrowIfNull(executionPolicy);
        executionPolicy.Validate();
        ArgumentNullException.ThrowIfNull(cacheExecutionOptions);
        var cacheExecutionPolicy = cacheExecutionOptions.Value;
        ArgumentNullException.ThrowIfNull(cacheExecutionPolicy);
        cacheExecutionPolicy.Validate();
        IOptions<ZoneTreeStorageExecutionOptions> frozenExecution = new OptionsWrapper<ZoneTreeStorageExecutionOptions>(executionPolicy);
        IOptions<ZoneTreePointCacheExecutionOptions> frozenCache = new OptionsWrapper<ZoneTreePointCacheExecutionOptions>(cacheExecutionPolicy);
        var resolved = options.ResolveExecutionOptions(frozenExecution);
        if (resolved.EmbeddedPointCache is { } embedded)
        {
            resolved = resolved with
            {
                EmbeddedPointCache = embedded.WithExecutionSnapshot(cacheExecutionPolicy)
            };
        }
        return (resolved, frozenExecution, frozenCache);
    }
}
