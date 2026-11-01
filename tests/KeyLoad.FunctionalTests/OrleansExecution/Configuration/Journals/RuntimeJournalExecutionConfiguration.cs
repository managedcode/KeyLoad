#pragma warning disable ORLEANSEXP005
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Journaling;
using Orleans.Metadata;
using Orleans.Serialization;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RuntimeJournalExecutionConfiguration
{
    internal static void Register(IServiceCollection services, NodeOptions _)
    {
        services.AddSerializer(serialization => serialization.AddAssembly(typeof(RuntimeJournalReplayGrain).Assembly));
        services.Configure<GrainTypeOptions>(options =>
        {
            options.AddClass(typeof(RuntimeJournalReplayGrain));
        });
        services.AddSingleton<IConfigureGrainTypeComponents>(provider =>
            new RuntimeJournalReplayActivatorConfiguration(provider.GetRequiredService<GrainClassMap>(), provider));
        services.AddOptions<RuntimeTestOptions>()
            .Validate(options => options.IsValid(), "The execution runtime test timing profile is invalid.")
            .ValidateOnStart();
        // DurableGrain uses the default binding of the same production journal singleton.
        services.AddSingleton(provider => provider.GetRequiredKeyedService<IJournalStorageProvider>(
            RuntimeJournalRegistration.ProviderName));
    }
}

#pragma warning restore ORLEANSEXP005
