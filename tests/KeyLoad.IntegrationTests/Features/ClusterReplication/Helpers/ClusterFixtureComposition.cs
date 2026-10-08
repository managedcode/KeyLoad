using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ClusterFixtureComposition
{
    private const string InvalidContainerModel = "The Aspire model must expose exactly the three named RF3 container resources.";

    internal static void ConfigureCommandAdmission(IDistributedApplicationTestingBuilder builder, long? commandBytes)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (commandBytes is not { } bytes)
        {
            return;
        }

        foreach (var number in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount))
        {
            var node = builder.CreateResourceBuilder(builder.Resources.OfType<ContainerResource>()
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

        foreach (var number in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount))
        {
            var node = builder.CreateResourceBuilder(builder.Resources.OfType<ContainerResource>()
                .Single(resource => resource.Name == ClusterFixtureProtocol.NodeName(number)));
            node.WithEnvironment(ClusterFixtureProtocol.HttpBodyBytesSetting,
                limits.MaxBodyBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.WithEnvironment(ClusterFixtureProtocol.HttpControlBodyBytesSetting,
                limits.MaxControlBodyBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.WithEnvironment(ClusterFixtureProtocol.HttpReservedBytesSetting,
                limits.MaxReservedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.WithEnvironment(ClusterFixtureProtocol.HttpHeavyReadBytesSetting,
                limits.HeavyReadReservedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
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

    internal static Dictionary<string, string> GetContainerNames(IDistributedApplicationTestingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var names = builder.Resources.OfType<ContainerResource>()
            .Where(resource => ClusterFixtureProtocol.IsNodeName(resource.Name))
            .ToDictionary(resource => resource.Name,
                resource => resource.Annotations.OfType<ContainerNameAnnotation>().Single().Name, StringComparer.Ordinal);
        if (names.Count != ClusterFixtureProtocol.NodeCount
            || Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount)
                .Any(number => !names.ContainsKey(ClusterFixtureProtocol.NodeName(number))))
        {
            throw new InvalidOperationException(InvalidContainerModel);
        }

        return names;
    }

    internal static void ConfigureLogging(IDistributedApplicationTestingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddFilter(ClusterFixtureProtocol.HealthCheckLoggerCategory, LogLevel.Critical);
        });
    }
}
