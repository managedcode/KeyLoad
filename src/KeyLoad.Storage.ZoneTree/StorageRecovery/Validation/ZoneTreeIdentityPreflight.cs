using Microsoft.Extensions.Options;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeIdentityPreflight
{
    internal static void Validate(ZoneTreeStoreOptions options,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Directory);
        ArgumentNullException.ThrowIfNull(executionOptions);
        ZoneTreeIdentityFile.ValidateBeforeOpen(options.ResolveExecutionOptions(executionOptions));
    }
}
