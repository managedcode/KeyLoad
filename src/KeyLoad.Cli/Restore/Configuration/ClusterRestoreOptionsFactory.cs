using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.Cli.Features.BackupRestore;

[ConfigurationBinding]
internal sealed class ClusterRestoreOptionsFactory(IConfiguration configuration,
    Func<ClusterRestoreOperatorConfiguration, bool> validate, string message)
    : OptionsFactory<ClusterRestoreOperatorConfiguration>(
        [new ConfigureNamedOptions<ClusterRestoreOperatorConfiguration>(Options.DefaultName,
            options => configuration.Bind(options))], [],
        [new ValidateOptions<ClusterRestoreOperatorConfiguration>(Options.DefaultName, validate, message)])
{
    protected override ClusterRestoreOperatorConfiguration CreateInstance(string name) => new();
}
