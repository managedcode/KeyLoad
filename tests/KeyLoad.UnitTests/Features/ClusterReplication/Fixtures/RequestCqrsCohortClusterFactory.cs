using Orleans.TestingHost;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class RequestCqrsCohortClusterFactory
{
    private const int SiloCount = 3;

    internal static TestCluster Create(string ownerId, bool hosted)
    {
        var builder = new TestClusterBuilder(SiloCount);
        builder.Options.ConnectionTransport = ConnectionTransportType.TcpSocket;
        builder.Properties[RequestCqrsCohortOwners.Key] = ownerId;
        builder.AddSiloBuilderConfigurator<RequestCqrsCohortNativeConfigurator>();
        if (hosted)
        {
            builder.AddClientBuilderConfigurator<global::KeyLoad.UnitTests.Features.ClusterRouting.RequestCqrsHostedClientConfigurator>();
        }
        return builder.Build();
    }
}
