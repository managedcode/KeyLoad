using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Exact typed container identities supplied to the startup graph.</summary>
[ConfigurationOptions]
internal sealed class ContainerImageOptions
{
    internal const string ServerKey = "KeyLoad:ContainerImages:Server";
    internal const string RunnerKey = "Benchmarks:ContainerImages:LoadGenerator";
    [ConfigurationKeyName(ServerKey)]
    public string? Server
    {
        get; set;
    }
    [ConfigurationKeyName(RunnerKey)]
    public string? Runner
    {
        get; set;
    }
}
