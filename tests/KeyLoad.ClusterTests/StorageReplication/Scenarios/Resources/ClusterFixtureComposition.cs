using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Client;
using KeyLoad.Query;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.ClusterTests.StorageReplication;

internal static class ClusterFixtureComposition
{
    private const string InvalidProjectModel = "The Aspire model must expose exactly the three named Orleans project resources.";

    internal static string[] PrepareArguments(string root, PublicQueryStorageSelection storage)
        => [$"{ClusterFixtureProtocol.DataRootArgument}{root}", ClusterFixtureProtocol.EphemeralArgument,
            ClusterFixtureProtocol.SiloCountArgument, ClusterFixtureProtocol.StorageModeArgument + storage.Mode,
            ClusterFixtureProtocol.ReplicaCountArgument + storage.ReplicaCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            KeyLoad.AppHost.TestInfrastructure.ProcessFaultCommandProtocol.EnabledArgument];

    internal static KeyLoadClient CreateClient(HttpClient transport, string key)
    {
        ArgumentNullException.ThrowIfNull(transport);
        HttpClient? pending = null;
        try
        {
            pending = transport;
            pending.Timeout = ClusterFixtureProtocol.ClientTimeout;
            var client = new KeyLoadClient(pending, key, IntegrationClientOptions.Execution());
            pending = null;
            return client;
        }
        finally { pending?.Dispose(); }
    }

    internal static void ConfigureBodyLimit(IDistributedApplicationTestingBuilder builder, int? maximumBodyBytes)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (maximumBodyBytes is not { } bytes)
        {
            return;
        }

        foreach (var number in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount))
        {
            var node = builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>()
                .Single(resource => resource.Name == ClusterFixtureProtocol.NodeName(number)));
            node.WithEnvironment(ClusterFixtureProtocol.BodyBytesSetting,
                bytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    internal static void ConfigureTestOverrides(IDistributedApplicationTestingBuilder builder, int? maximumBodyBytes,
        QueryExecutionOptions? queryExecution = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ConfigureBodyLimit(builder, maximumBodyBytes);
        ClusterFixtureQueryResultLimits.Configure(builder, queryExecution);
    }

    internal static string[] GetResourceNames(IDistributedApplicationTestingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var names = builder.Resources.OfType<ProjectResource>()
            .Where(resource => ClusterFixtureProtocol.IsNodeName(resource.Name))
            .Select(resource => resource.Name).ToArray();
        if (names.Length != ClusterFixtureProtocol.NodeCount
            || Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount)
                .Any(number => !names.Contains(ClusterFixtureProtocol.NodeName(number), StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(InvalidProjectModel);
        }
        return names;
    }

    internal static void ConfigureLogging(IDistributedApplicationTestingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Configuration[ClusterFixtureStartupProfile.EnabledSetting] = bool.TrueString;
        builder.Configuration[ClusterFixtureStartupProfile.SessionSetting] = Guid.NewGuid().ToString(ClusterFixtureProtocol.GuidFormat);
        builder.Services.AddSingleton(static services =>
            new ClusterFixtureStartupEvidence(services.GetRequiredService<IConfiguration>()));
        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddFilter(ClusterFixtureProtocol.HealthCheckLoggerCategory, LogLevel.Critical);
        });
    }
}
