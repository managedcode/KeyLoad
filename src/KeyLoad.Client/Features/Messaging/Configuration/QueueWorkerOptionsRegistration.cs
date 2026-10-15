using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Client;

/// <summary>Owns queue worker configuration at application composition.</summary>
[ConfigurationBinding]
public static class QueueWorkerOptionsRegistration
{
    /// <summary>Binds and validates the native caller policy without registering a server worker.</summary>
    /// <param name="services">Application service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The original service collection.</returns>
    public static IServiceCollection AddKeyLoadQueueWorkerOptions(this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<QueueWorkerOptions>()
            .Bind(configuration.GetSection(QueueWorkerOptions.SectionName))
            .Validate(options => options.IsValid(), QueueWorkerOptions.ValidationMessage)
            .ValidateOnStart();
        return services;
    }
}
