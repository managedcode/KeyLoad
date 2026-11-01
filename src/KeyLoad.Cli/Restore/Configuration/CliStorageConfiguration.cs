using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.Cli.BackupRestore;

/// <summary>Binds optional standalone storage environment overrides before opening files.</summary>
[ConfigurationBinding]
internal static class CliStorageConfiguration
{
    private const string EnvironmentPrefix = "KEYLOAD_STORAGE__";
    internal static CliStorageRuntimeOptions Read()
    {
        return new(
        Bind(EnvironmentPrefix, static () => new ZoneTreeStorageExecutionOptions(),
            options => options.IsValid(), ZoneTreeStorageExecutionOptions.ValidationMessage));
    }

    internal static OptionsManager<T> Bind<T>(string prefix, Func<T> initialize, Func<T, bool> validate, string message)
        where T : class =>
        Bind<T>(prefix, configuration => new CliStorageOptionsFactory<T>(initialize, configuration, validate, message));

    internal static OptionsManager<T> Bind<T>(string prefix, Func<IConfiguration, IOptionsFactory<T>> createFactory)
        where T : class
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables(prefix).Build();
        using var configurationLifetime = configuration as IDisposable;
        var options = new OptionsManager<T>(createFactory(configuration));
        _ = options.Value;
        return options;
    }
}
