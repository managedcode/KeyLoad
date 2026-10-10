using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueDeadlineRf3Selection(string PrincipalId)
{
    private const string PrincipalSetting = "KeyLoad__DueCoordination__QueueDeadlinePrincipalId";

    internal void Configure(IDistributedApplicationTestingBuilder builder)
    {
        var validated = new DueCoordinationOptions { QueueDeadlinePrincipalId = PrincipalId };
        if (!validated.IsValid())
        { throw new InvalidOperationException(DueCoordinationOptions.ValidationMessage); }
        foreach (var number in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount))
        {
            var node = builder.CreateResourceBuilder(builder.Resources.OfType<ContainerResource>()
                .Single(resource => resource.Name == ClusterFixtureProtocol.NodeName(number)));
            node.WithEnvironment(PrincipalSetting, PrincipalId);
        }
    }
}
