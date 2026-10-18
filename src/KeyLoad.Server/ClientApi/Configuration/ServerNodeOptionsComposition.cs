using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Uses the native options factory and canonical node postconfiguration before native silo registration.</summary>
[ConfigurationBinding]
internal static class ServerNodeOptionsComposition
{
    internal static IOptions<NodeOptions> Read(IConfiguration configuration)
    {
        var factory = new OptionsFactory<NodeOptions>(
            [new ConfigureFromConfigurationOptions<NodeOptions>(configuration.GetSection(ServerProtocol.ConfigurationSection))],
            [new PostConfigureOptions<NodeOptions>(Options.DefaultName,
                ServerRuntimeOptionsRegistration.ConfigureNode)],
            [new NodeOptionsValidator()]);
        return new OptionsManager<NodeOptions>(factory);
    }
}
