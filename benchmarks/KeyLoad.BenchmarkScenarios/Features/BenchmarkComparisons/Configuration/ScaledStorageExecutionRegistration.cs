using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Binds scaled-fixture policy from the actual native generated runner's environment.</summary>
[ConfigurationBinding]
internal static class ScaledStorageExecutionRegistration
{
    internal static IOptions<ScaledStorageExecutionOptions> Read()
    {
        using var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        return BenchmarkScenarioOptionsRegistration.Read<ScaledStorageExecutionOptions>(configuration,
            ScaledStorageExecutionOptions.SectionName, settings => settings.IsValid(),
            ScaledStorageExecutionOptions.ValidationMessage);
    }
}
