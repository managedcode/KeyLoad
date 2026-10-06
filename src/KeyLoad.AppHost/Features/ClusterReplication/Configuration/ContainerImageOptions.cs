using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Exact typed container identities supplied to the startup graph.</summary>
[ConfigurationOptions]
internal sealed class ContainerImageOptions
{
    internal const string ServerKey = "KeyLoad:ContainerImages:Server";
    internal const string RunnerKey = "Benchmarks:ContainerImages:LoadGenerator";
    internal const string VoterOneKey = "KeyLoadTests:ProtocolCohort:Voters:node1";
    internal const string VoterTwoKey = "KeyLoadTests:ProtocolCohort:Voters:node2";
    internal const string VoterThreeKey = "KeyLoadTests:ProtocolCohort:Voters:node3";
    [ConfigurationKeyName(ServerKey)] public string? Server { get; set; }
    [ConfigurationKeyName(RunnerKey)] public string? Runner { get; set; }
    [ConfigurationKeyName(VoterOneKey)] public string? VoterOne { get; set; }
    [ConfigurationKeyName(VoterTwoKey)] public string? VoterTwo { get; set; }
    [ConfigurationKeyName(VoterThreeKey)] public string? VoterThree { get; set; }
}
