using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ClusterFixtureQueryResultLimits
{
    private const string ApproximateSearchSetting = "KeyLoad__QueryExecution__EnableApproximateSearch";

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
        foreach (var number in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount))
        {
            var node = builder.CreateResourceBuilder(builder.Resources.OfType<ContainerResource>()
                .Single(resource => resource.Name == ClusterFixtureProtocol.NodeName(number)));
            node.WithEnvironment(ApproximateSearchSetting, options.EnableApproximateSearch.ToString());
            if (options.MaximumResultBytes is { } maximum)
            { node.WithEnvironment(MaximumResultBytesSetting, maximum.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        }
    }
}
