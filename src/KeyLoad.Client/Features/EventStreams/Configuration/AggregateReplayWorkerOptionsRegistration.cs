using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Client;

/// <summary>Binds replay budgets at the native caller composition boundary.</summary>
[ConfigurationBinding]
public static class AggregateReplayWorkerOptionsRegistration
{
    /// <summary>Registers native replay worker options with startup validation.</summary>
    /// <param name="services">The caller's service collection.</param>
    /// <param name="configuration">The caller's configuration sources.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddKeyLoadAggregateReplayWorkerOptions(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AggregateReplayWorkerLimits>()
            .Bind(configuration.GetSection(AggregateReplayWorkerLimits.SectionName))
            .Validate(options => options.IsValid(), AggregateReplayWorkerLimits.ValidationMessage)
            .ValidateOnStart();
        return services;
    }
}
