using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Client;

/// <summary>Binds the SDK transport policy through native options at the caller's composition root.</summary>
[ConfigurationBinding]
public static class KeyLoadClientOptionsRegistration
{
    /// <summary>Registers and validates the SDK transport budget before a native host starts.</summary>
    /// <param name="services">The caller's native service collection.</param>
    /// <param name="configuration">The caller's configuration sources.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddKeyLoadClientExecutionOptions(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<KeyLoadClientExecutionOptions>()
            .Bind(configuration.GetSection(KeyLoadClientExecutionOptions.SectionName))
            .Validate(options => options.IsValid(), KeyLoadClientExecutionOptions.ValidationMessage)
            .ValidateOnStart();
        return services;
    }
}
