using KeyLoad.Orleans;
using KeyLoad.AppHost.Features.ClusterReplication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

/// <summary>Binds the isolated workload's native replay pools before any container is composed.</summary>
[ConfigurationBinding]
internal static class IsolatedKeyLoadReplayOptionsRegistration
{
    internal const string SectionName = "Benchmarks:IsolatedReplayAdmission";
    private const int DefaultCriticalCapacity = 16_384;
    private const int DefaultForwardCapacity = 32_768;
    private const int DefaultReadBarrierCapacity = 196_608;
    private const int DefaultDataAppendCapacity = 32_768;

    internal static IOptions<ReplicaReplayLimits> Bind(IConfiguration configuration)
    {
        var factory = new IsolatedReplayFactory(configuration.GetSection(SectionName));
        var options = new OptionsManager<ReplicaReplayLimits>(factory);
        _ = options.Value;
        return options;
    }

    private static bool IsValid(ReplicaReplayLimits value)
    {
        try { value.Validate(ClusterDeploymentOptions.VoterCount); }
        catch (InvalidOperationException) { return false; }
        return value.CriticalPerVoter <= DefaultCriticalCapacity && value.ForwardPerVoter <= DefaultForwardCapacity
            && value.ReadBarrierPerVoter <= DefaultReadBarrierCapacity && value.DataAppendPerVoter <= DefaultDataAppendCapacity;
    }

    private sealed class IsolatedReplayFactory(IConfiguration section) : OptionsFactory<ReplicaReplayLimits>(
        [new ConfigureFromConfigurationOptions<ReplicaReplayLimits>(section)], [],
        [new ValidateOptions<ReplicaReplayLimits>(Options.DefaultName,
            IsValid, ReplicaTransportProtocol.InvalidReplayLimits)])
    {
        protected override ReplicaReplayLimits CreateInstance(string name)
            => new()
            {
                CriticalPerVoter = DefaultCriticalCapacity, ForwardPerVoter = DefaultForwardCapacity,
                ReadBarrierPerVoter = DefaultReadBarrierCapacity, DataAppendPerVoter = DefaultDataAppendCapacity
            };
    }
}
