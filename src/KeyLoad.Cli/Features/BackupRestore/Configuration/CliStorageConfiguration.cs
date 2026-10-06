using KeyLoad;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Binds optional standalone storage environment overrides before opening files.</summary>
[ConfigurationBinding]
internal static class CliStorageConfiguration
{
    private const string EnvironmentPrefix = "KEYLOAD_STORAGE__";
    private const string CacheEnvironmentPrefix = "KEYLOAD_POINTCACHE__";
    private static readonly string[] PropertyNames =
    [
        nameof(ZoneTreeStorageExecutionOptions.MaxFrameBytes),
        nameof(ZoneTreeStorageExecutionOptions.MaxSnapshotBytes),
        nameof(ZoneTreeStorageExecutionOptions.CheckpointBatchBytes),
        nameof(ZoneTreeStorageExecutionOptions.CheckpointBatchRecords),
        nameof(ZoneTreeStorageExecutionOptions.MaximumRangeRecords),
        nameof(ZoneTreeStorageExecutionOptions.MaximumRangeWorkBytes),
        nameof(ZoneTreeStorageExecutionOptions.MaximumReadCutRecords),
        nameof(ZoneTreeStorageExecutionOptions.MaximumReadCutExaminedBytes),
        nameof(ZoneTreeStorageExecutionOptions.MaximumReadCutElapsed)
    ];

    private static readonly string[] CachePropertyNames =
    [
        nameof(ZoneTreePointCacheExecutionOptions.MaxEntries),
        nameof(ZoneTreePointCacheExecutionOptions.MaxRetainedBytes),
        nameof(ZoneTreePointCacheExecutionOptions.MaxKeyBytes),
        nameof(ZoneTreePointCacheExecutionOptions.MaxValueBytes),
        nameof(ZoneTreePointCacheExecutionOptions.MaxPinsPerEntry),
        nameof(ZoneTreePointCacheExecutionOptions.MaximumVictimAttempts)
    ];

    internal static CliStorageRuntimeOptions Read() => new(
        Bind<ZoneTreeStorageExecutionOptions>(EnvironmentPrefix, PropertyNames,
            options => options.IsValid(), ZoneTreeStorageExecutionOptions.ValidationMessage),
        Bind<ZoneTreePointCacheExecutionOptions>(CacheEnvironmentPrefix, CachePropertyNames,
            options => options.IsValid(), ZoneTreePointCacheExecutionOptions.ValidationMessage));

    private static IOptions<T> Bind<T>(string prefix, string[] propertyNames, Func<T, bool> validate, string message)
        where T : class
    {
        var values = propertyNames.Select(name => new KeyValuePair<string, string?>(name,
            Environment.GetEnvironmentVariable(prefix + name.ToUpperInvariant())));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        using var configurationLifetime = configuration as IDisposable;
        var options = new OptionsManager<T>(new OptionsFactory<T>(
            [new ConfigureFromConfigurationOptions<T>(configuration)], [],
            [new ValidateOptions<T>(Options.DefaultName, validate, message)]));
        _ = options.Value;
        return options;
    }
}

internal sealed record CliStorageRuntimeOptions(
    IOptions<ZoneTreeStorageExecutionOptions> Storage, IOptions<ZoneTreePointCacheExecutionOptions> PointCache);
