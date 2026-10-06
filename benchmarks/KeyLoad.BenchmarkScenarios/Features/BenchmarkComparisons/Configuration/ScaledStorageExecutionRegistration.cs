using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Binds scaled-fixture policy from the actual native generated runner's environment.</summary>
[ConfigurationBinding]
internal static class ScaledStorageExecutionRegistration
{
    internal static IOptions<ScaledStorageExecutionOptions> Read()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        using var configurationLifetime = configuration as IDisposable;
        return BenchmarkScenarioOptionsRegistration.Read<ScaledStorageExecutionOptions>(configuration,
            ScaledStorageExecutionOptions.SectionName, settings => settings.IsValid(),
            ScaledStorageExecutionOptions.ValidationMessage);
    }
}
