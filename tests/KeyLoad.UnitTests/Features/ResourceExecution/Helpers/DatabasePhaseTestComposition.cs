using System.Globalization;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Projects centrally bound and validated native settings into independent BCL banks.</summary>
[ConfigurationBinding]
internal static class DatabasePhaseTestComposition
{
    internal static DatabasePhaseBank Create(bool enabled) => Create(Bind(
        (nameof(DatabasePhaseExecutionOptions.Enabled), enabled.ToString(CultureInfo.InvariantCulture))));

    internal static DatabasePhaseBank Create(bool enabled, int stripeCount, int maximumCasAttempts) =>
        Create(Bind(enabled, stripeCount, maximumCasAttempts));

    internal static IOptions<DatabasePhaseExecutionOptions> Bind(bool enabled, int stripeCount, int maximumCasAttempts) => Bind(
        (nameof(DatabasePhaseExecutionOptions.Enabled), enabled.ToString(CultureInfo.InvariantCulture)),
        (nameof(DatabasePhaseExecutionOptions.StripeCount), stripeCount.ToString(CultureInfo.InvariantCulture)),
        (nameof(DatabasePhaseExecutionOptions.MaximumCasAttempts), maximumCasAttempts.ToString(CultureInfo.InvariantCulture)));

    internal static DatabasePhaseBank Create(IOptions<DatabasePhaseExecutionOptions> options)
    {
        var captured = options.Value;
        captured.Validate();
        return new DatabasePhaseBank(captured.Enabled, captured.StripeCount, captured.MaximumCasAttempts);
    }

    internal static IOptions<DatabasePhaseExecutionOptions> Bind(params (string Property, string Value)[] values)
    {
        var input = values.Select(value => new KeyValuePair<string, string?>(
            DatabasePhaseExecutionOptions.SectionName + ConfigurationPath.KeyDelimiter + value.Property, value.Value));
        return BindRoot(input);
    }

    internal static IOptions<DatabasePhaseExecutionOptions> BindRoot(IEnumerable<KeyValuePair<string, string?>> input)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(input).Build();
        using var lifetime = configuration as IDisposable;
        var services = new ServiceCollection();
        ServerRuntimeOptionsRegistration.AddDatabasePhaseOptions(services, configuration);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();
        var options = provider.GetRequiredService<IOptions<DatabasePhaseExecutionOptions>>();
        options.Value.Validate();
        return options;
    }
}
