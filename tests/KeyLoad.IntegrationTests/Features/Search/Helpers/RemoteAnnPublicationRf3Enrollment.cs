using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class RemoteAnnPublicationRf3Enrollment
{
    private const string ApproximateSearchSetting = "KeyLoad__QueryExecution__EnableApproximateSearch";

    internal static void Configure(IDistributedApplicationTestingBuilder builder,
        IOptions<QueryExecutionOptions>? configured)
    {
        if (configured is null)
        { return; }
        var options = configured.Value;
        options.Validate();
        var nodes = builder.Resources.OfType<ContainerResource>()
            .Where(resource => TwoRf3MembershipProtocol.Nodes.Contains(resource.Name, StringComparer.Ordinal)).ToArray();
        if (nodes.Length != TwoRf3MembershipProtocol.NodeCount
            || !nodes.Select(node => node.Name).Order(StringComparer.Ordinal).SequenceEqual(
                TwoRf3MembershipProtocol.Nodes.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        { throw new InvalidOperationException(TwoRf3MembershipProtocol.ImageMismatch); }
        foreach (var resource in nodes)
        {
            builder.CreateResourceBuilder(resource).WithEnvironment(ApproximateSearchSetting,
                options.EnableApproximateSearch.ToString());
        }
    }
}
