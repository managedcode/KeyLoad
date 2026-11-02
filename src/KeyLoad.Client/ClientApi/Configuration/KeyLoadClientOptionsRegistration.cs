using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Client;

/// <summary>Binds the SDK transport policy through execution options at the caller's composition root.</summary>
[ConfigurationBinding]
public static class KeyLoadClientOptionsRegistration
{
    /// <summary>Registers and validates the SDK transport budget before a execution host starts.</summary>
    /// <param name="services">The caller's execution service collection.</param>
    /// <param name="configuration">The caller's configuration sources.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddKeyLoadClientExecutionOptions(this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services) /* KLD0041: BCL null validation accepts the services IServiceCollection reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(configuration) /* KLD0041: BCL null validation accepts the configuration IConfiguration reference through its object parameter. */;
        services.AddOptions<KeyLoadClientExecutionOptions>()
            .Bind(configuration.GetSection(KeyLoadClientExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), KeyLoadClientExecutionOptions.ValidationMessage)
            .ValidateOnStart();
        return services;
    }
}
