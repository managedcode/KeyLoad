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
    private const string BackupEnvironmentPrefix = "KEYLOAD_BACKUP__";
    internal static CliStorageRuntimeOptions Read()
    {
        _ = SerializationExecutionRegistration.Process.Value;
        return new(
        Bind(EnvironmentPrefix, static () => new ZoneTreeStorageExecutionOptions(),
            options => options.IsValid(), ZoneTreeStorageExecutionOptions.ValidationMessage),
        Bind(CacheEnvironmentPrefix, static () => new ZoneTreePointCacheExecutionOptions(),
            options => options.IsValid(), ZoneTreePointCacheExecutionOptions.ValidationMessage),
        Bind(BackupEnvironmentPrefix, static () => new CliBackupExecutionOptions(),
            options => options.IsValid(), CliBackupExecutionOptions.ValidationMessage));
    }

    private static OptionsManager<T> Bind<T>(string prefix, Func<T> initialize, Func<T, bool> validate, string message)
        where T : class
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
        using var configurationLifetime = configuration as IDisposable;
        var options = new OptionsManager<T>(new CliStorageOptionsFactory<T>(initialize, configuration, validate, message));
        _ = options.Value;
        return options;
    }
}

internal sealed record CliStorageRuntimeOptions(
    IOptions<ZoneTreeStorageExecutionOptions> Storage, IOptions<ZoneTreePointCacheExecutionOptions> PointCache,
    IOptions<CliBackupExecutionOptions> Backup);
