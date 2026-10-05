using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Client;

/// <summary>Binds client expression budgets at the native caller composition boundary.</summary>
[ConfigurationBinding]
public static class QueryTranslationOptionsRegistration
{
    /// <summary>Registers native query translation options with startup validation.</summary>
    /// <param name="services">The caller's service collection.</param>
    /// <param name="configuration">The caller's configuration sources.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddKeyLoadQueryTranslationOptions(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<QueryTranslationOptions>()
            .Bind(configuration.GetSection(QueryTranslationOptions.SectionName))
            .Validate(options => options.IsValid(), QueryTranslationOptions.ValidationMessage)
            .ValidateOnStart();
        return services;
    }
}
