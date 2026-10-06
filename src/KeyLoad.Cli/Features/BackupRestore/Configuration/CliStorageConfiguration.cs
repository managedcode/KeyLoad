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
    internal static CliStorageRuntimeOptions Read() => new(
        Bind<ZoneTreeStorageExecutionOptions>(EnvironmentPrefix,
            options => options.IsValid(), ZoneTreeStorageExecutionOptions.ValidationMessage),
        Bind<ZoneTreePointCacheExecutionOptions>(CacheEnvironmentPrefix,
            options => options.IsValid(), ZoneTreePointCacheExecutionOptions.ValidationMessage));

    private static OptionsManager<T> Bind<T>(string prefix, Func<T, bool> validate, string message)
        where T : class
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
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
