using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ClusterFixtureQueryResultLimits
{
    private const string MaximumResultBytesSetting = "KeyLoad__QueryExecution__MaximumResultBytes";

    internal static QueryExecutionOptions Validate(QueryExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return options;
    }

    internal static void Configure(IDistributedApplicationTestingBuilder builder, QueryExecutionOptions? options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (options is null)
        {
            return;
        }
        Validate(options);
        if (options.MaximumResultBytes is not { } maximum)
        {
            return;
        }
        foreach (var number in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount))
        {
            var node = builder.CreateResourceBuilder(builder.Resources.OfType<ContainerResource>()
                .Single(resource => resource.Name == ClusterFixtureProtocol.NodeName(number)));
            node.WithEnvironment(MaximumResultBytesSetting, maximum.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
