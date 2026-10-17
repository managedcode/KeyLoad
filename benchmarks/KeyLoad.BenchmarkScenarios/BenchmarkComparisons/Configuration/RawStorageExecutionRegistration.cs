using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Binds raw-fixture admission through the native generated runner's environment.</summary>
[ConfigurationBinding]
internal static class RawStorageExecutionRegistration
{
    internal static IOptions<RawStorageExecutionOptions> Read()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        using var configurationLifetime = configuration as IDisposable;
        return BenchmarkScenarioOptionsRegistration.Read<RawStorageExecutionOptions>(configuration,
            RawStorageExecutionOptions.SectionName, settings => settings.IsValid(),
            RawStorageExecutionOptions.ValidationMessage);
    }
}
