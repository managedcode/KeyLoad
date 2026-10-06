using KeyLoad.AppHost.Hosting;
using System.Globalization;
using System.Text;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedOpenSearchResources
{
    private const string Target = "OpenSearch";
    private const string NodePrefix = "isolated-opensearch-";
    private const string Image = "opensearchproject/opensearch";
    private const string Tag = "3.6.0";
    private const string Http = "http";
    private const string Data = "/usr/share/opensearch/data";
    private const string VolumeSuffix = "-data-";
    private const string VolumePrefix = "keyload-";
    private const string HealthPath = "/";
    private const string GuidFormat = "N";
    private const string TagSeparator = ":";
    private const string DigestSeparator = "@";
    private const int DigestPrefixLength = 7;
    private const int Port = 9200;
    private const string ClusterPrefix = "isolated-comparison-";
    private const string ClusterName = "cluster.name";
    private const string NodeName = "node.name";
    private const string NetworkHost = "network.host";
    private const string BindAll = "0.0.0.0";
    private const string HeapSetting = "OPENSEARCH_JAVA_OPTS";
    private const string HeapFormat = "-Xms{0}m -Xmx{0}m";
    private static readonly CompositeFormat HeapCompositeFormat = CompositeFormat.Parse(HeapFormat);
    private const string DisableDemo = "DISABLE_INSTALL_DEMO_CONFIG";
    private const string DisableSecurity = "DISABLE_SECURITY_PLUGIN";
    private const string Enabled = "true";
    private const string Discovery = "discovery.seed_hosts";
    private const string Managers = "cluster.initial_cluster_manager_nodes";
    private const string DiscoveryType = "discovery.type";
    private const string Single = "single-node";
    private const string InvalidSelection = "IsolatedOpenSearchSelectionInvalid";

    internal static void Add(IsolatedResourceContext context)
    {
        const int StartValue = 1;
        const int IndexInitialValue = 0;

        ArgumentNullException.ThrowIfNull(context);
        context.Selection.Validate();
        if (context.Selection.Target != Target)
        {
            throw new InvalidOperationException(InvalidSelection);
        }
        var deployment = AppHostOptionsRegistration.Get(context.Builder).Deployment.Value;
        var heap = string.Format(CultureInfo.InvariantCulture, HeapCompositeFormat, deployment.OpenSearchHeapMegabytes);
        var cell = Guid.NewGuid().ToString(GuidFormat);
        var names = Enumerable.Range(StartValue, context.Selection.NodeCount)
            .Select(index => NodePrefix + index.ToString(CultureInfo.InvariantCulture)).ToArray();
        for (var index = IndexInitialValue; index < names.Length; index++)
        {
            var name = names[index];
            var node = context.Builder.AddContainer(name, Image, Tag).WithImageSHA256(BenchmarkResources.OpenSearchDigest[DigestPrefixLength..])
                .WithContainerNetworkAlias(name).WithVolume(VolumePrefix + name + VolumeSuffix + cell, Data)
                .WithHttpEndpoint(targetPort: Port, name: Http).WithHttpHealthCheck(HealthPath)
                .WithEnvironment(ClusterName, ClusterPrefix + cell).WithEnvironment(NodeName, name)
                .WithEnvironment(NetworkHost, BindAll).WithEnvironment(HeapSetting, heap)
                .WithEnvironment(DisableDemo, Enabled).WithEnvironment(DisableSecurity, Enabled);
            ConfigureDiscovery(node, names);
            context.BindEndpoint(index, node, Http);
        }
        context.BindImage(Image + TagSeparator + Tag + DigestSeparator + BenchmarkResources.OpenSearchDigest);
    }

    private static void ConfigureDiscovery(IResourceBuilder<ContainerResource> node, string[] names)
    {
        const int SingleNodeCount = 1;
        const char SeparatorCharacter = ',';

        if (names.Length == SingleNodeCount)
        {
            node.WithEnvironment(DiscoveryType, Single);
            return;
        }
        var seeds = string.Join(SeparatorCharacter, names);
        node.WithEnvironment(Discovery, seeds).WithEnvironment(Managers, seeds);
    }
}
