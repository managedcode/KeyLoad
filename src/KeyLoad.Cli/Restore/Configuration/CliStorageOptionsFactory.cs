using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Creates explicit native policy defaults before standard binding and validation.</summary>
[ConfigurationBinding]
internal sealed class CliStorageOptionsFactory<T>(Func<T> initialize, IConfiguration configuration,
    Func<T, bool> validate, string message) : OptionsFactory<T>(
        [new ConfigureFromConfigurationOptions<T>(configuration)], [],
        [new ValidateOptions<T>(Options.DefaultName, validate, message)]) where T : class
{
    protected override T CreateInstance(string name) => initialize();
}
