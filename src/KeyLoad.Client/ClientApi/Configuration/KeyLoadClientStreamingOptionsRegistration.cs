using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Client;

/// <summary>Binds the native finite SDK streaming policy at the caller composition root.</summary>
[ConfigurationBinding]
public static class KeyLoadClientStreamingOptionsRegistration
{
    /// <summary>Registers existing native frame/body limits and requires an explicit finite total stream grant.</summary>
    /// <param name="services">The caller's actual service collection.</param>
    /// <param name="configuration">The caller's centrally configured native policy.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddKeyLoadClientStreamingOptions(this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<CqrsStreamClientOptions>()
            .Bind(configuration.GetSection(KeyLoadClientStreamPolicy.SectionName))
            .Validate(KeyLoadClientStreamPolicy.IsValid, KeyLoadClientStreamPolicy.Invalid)
            .ValidateOnStart();
        return services;
    }
}
