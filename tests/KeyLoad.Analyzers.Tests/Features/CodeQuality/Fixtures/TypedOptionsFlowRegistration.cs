using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

[KeyLoad.ConfigurationBinding]
internal static class TypedOptionsFlowRegistration
{
    internal const string CapacityKey = nameof(TypedOptionsFlowSettings.AdmissionCapacity);
    internal const string DeadlineKey = nameof(TypedOptionsFlowSettings.Deadline);
    private const string InvalidPolicy = "Admission capacity and deadline must be positive.";

    internal static ServiceProvider CreateProvider(TypedOptionsFlowState state,
        string? capacity = null, string? deadline = null)
    {
        var values = new Dictionary<string, string?>();
        if (capacity is not null)
        {
            values.Add(CapacityKey, capacity);
        }

        if (deadline is not null)
        {
            values.Add(DeadlineKey, deadline);
        }

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(_ => new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        services.AddOptions<TypedOptionsFlowSettings>()
            .Configure<IConfiguration>(static (policy, configuration) => configuration.Bind(policy))
            .Validate(static policy => policy.AdmissionCapacity > 0 && policy.Deadline > TimeSpan.Zero, InvalidPolicy);
        services.AddSingleton(state);
        services.AddSingleton<TypedOptionsFlowConsumer>();
        return services.BuildServiceProvider();
    }
}
