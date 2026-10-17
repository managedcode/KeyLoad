namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedQdrantBootstrap
{
    private const string Entrypoint = "/qdrant/entrypoint.sh";
    private const string EnabledSetting = "QDRANT__CLUSTER__ENABLED";
    private const string TrueValue = "true";
    private const string FalseValue = "false";
    private const string UriArgument = "--uri";
    private const string BootstrapArgument = "--bootstrap";
    private const string UriPrefix = "http://";
    private const string PeerPort = ":6335";
    private const string FirstPeer = UriPrefix + "isolated-qdrant-1" + PeerPort;

    internal static void Configure(IResourceBuilder<QdrantServerResource> node, string name, int count, int ordinal)
    {
        const int SingleNodeCount = 1;
        const int BoundaryValue = 1;

        node.WithContainerNetworkAlias(name).WithEntrypoint(Entrypoint)
            .WithEnvironment(EnabledSetting, count == SingleNodeCount ? FalseValue : TrueValue);
        if (count == SingleNodeCount)
        {
            return;
        }
        node.WithArgs(UriArgument, UriPrefix + name + PeerPort);
        if (ordinal > BoundaryValue)
        {
            node.WithArgs(BootstrapArgument, FirstPeer);
        }
    }
}
