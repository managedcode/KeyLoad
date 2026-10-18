using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Client;
using KeyLoad.Query;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ClusterFixtureComposition
{
    private const string InvalidProjectModel = "The Aspire model must expose exactly the three named Orleans project resources.";

    internal static string[] PrepareArguments(string root, PublicQueryStorageSelection storage)
        => [$"{ClusterFixtureProtocol.DataRootArgument}{root}", ClusterFixtureProtocol.EphemeralArgument,
            ClusterFixtureProtocol.SiloCountArgument, ClusterFixtureProtocol.StorageModeArgument + storage.Mode,
            ClusterFixtureProtocol.ReplicaCountArgument + storage.ReplicaCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            KeyLoad.AppHost.Features.ClusterReplication.ProcessFaultCommandProtocol.EnabledArgument];

    internal static async Task<IDistributedApplicationTestingBuilder> CreateBuilderAsync(
        string[] arguments, KeyLoad.IntegrationTests.Features.Authorization.FoundationOtlpClusterSelection? telemetry,
        CancellationToken token)
    {
        var builder = telemetry is null
            ? await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(arguments, token)
            : await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(
                KeyLoad.IntegrationTests.Features.Authorization.FoundationOtlpClusterConfiguration.Options(telemetry.Value), arguments, token);
        return builder;
    }

    internal static KeyLoadClient CreateClient(DistributedApplication application, string node, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        HttpClient? pending = null;
        try
        {
            pending = application.CreateHttpClient(node, ClusterFixtureProtocol.HttpEndpointName);
            pending.Timeout = ClusterFixtureProtocol.ClientTimeout;
            var client = new KeyLoadClient(pending, key, IntegrationClientOptions.Execution());
            pending = null;
            return client;
        }
        finally { pending?.Dispose(); }
    }

    internal static void ConfigureCommandAdmission(IDistributedApplicationTestingBuilder builder, long? commandBytes)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (commandBytes is not { } bytes)
        {
            return;
        }

        foreach (var number in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount))
        {
            var node = builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>()
                .Single(resource => resource.Name == ClusterFixtureProtocol.NodeName(number)));
            node.WithEnvironment(ClusterFixtureProtocol.CommandBytesSetting,
                bytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    internal static void ConfigureHttpAdmission(IDistributedApplicationTestingBuilder builder, HttpAdmissionLimits? httpAdmission)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (httpAdmission is not { } limits)
        {
            return;
        }

        limits.Validate();
        foreach (var number in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount))
        {
            var node = builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>()
                .Single(resource => resource.Name == ClusterFixtureProtocol.NodeName(number)));
            node.WithEnvironment(ClusterFixtureProtocol.HttpBodyBytesSetting,
                limits.MaxBodyBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.WithEnvironment(ClusterFixtureProtocol.HttpControlBodyBytesSetting,
                limits.MaxControlBodyBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.WithEnvironment(ClusterFixtureProtocol.HttpReservedBytesSetting,
                limits.MaxReservedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.WithEnvironment(ClusterFixtureProtocol.HttpHeavyReadBytesSetting,
                limits.HeavyReadReservedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.WithEnvironment(ClusterFixtureProtocol.HttpRequestsSetting,
                limits.MaxRequests.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.WithEnvironment(ClusterFixtureProtocol.HttpTenantRequestsSetting,
                limits.MaxTenantRequests.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.WithEnvironment(ClusterFixtureProtocol.HttpPrincipalRequestsSetting,
                limits.MaxPrincipalRequests.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.WithEnvironment(ClusterFixtureProtocol.HttpControlRequestsSetting,
                limits.ReservedControlRequests.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.WithEnvironment(ClusterFixtureProtocol.HttpControlReservedBytesSetting,
                limits.ReservedControlBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.WithEnvironment(ClusterFixtureProtocol.HttpTenantControlRequestsSetting,
                limits.MaxTenantControlRequests.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.WithEnvironment(ClusterFixtureProtocol.HttpPrincipalControlRequestsSetting,
                limits.MaxPrincipalControlRequests.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.WithEnvironment(ClusterFixtureProtocol.HttpOtherReservedBytesSetting,
                limits.OtherReservedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    internal static void ConfigureTestOverrides(IDistributedApplicationTestingBuilder builder, long? commandBytes,
        HttpAdmissionLimits? httpAdmission, DatabaseLimits? databaseLimits, QueryExecutionOptions? queryExecution = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ConfigureCommandAdmission(builder, commandBytes);
        ConfigureHttpAdmission(builder, httpAdmission);
        ClusterFixtureQueryResultLimits.Configure(builder, queryExecution);
        if (databaseLimits is not null)
        {
            ClusterFixtureDatabaseLimits.Configure(builder, databaseLimits);
        }
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
