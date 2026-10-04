using System.Globalization;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedRabbitBootstrap
{
    private const string DirectoryName = "rabbit-bootstrap";
    private const string FileName = "isolated-cluster.conf";
    private const string ConfigTarget = "/etc/rabbitmq/" + FileName;
    private const string ConfigStem = "/etc/rabbitmq/isolated-cluster";
    private const string Discovery = "cluster_formation.peer_discovery_backend = classic_config";
    private const string Disc = "cluster_formation.node_type = disc";
    private const string PeerPrefix = "cluster_formation.classic_config.nodes.";
    private const string NodePrefix = "isolated-rabbit-";
    private const string RabbitPrefix = "rabbit@";
    private const string NodeNameSetting = "RABBITMQ_NODENAME";
    private const string LongNameSetting = "RABBITMQ_USE_LONGNAME";
    private const string CookieSetting = "RABBITMQ_ERLANG_COOKIE";
    private const string ConfigSetting = "RABBITMQ_CONFIG_FILE";
    private const string FalseValue = "false";

    internal static string Write(IsolatedResourceContext context)
    {
        var lines = new List<string> { Discovery, Disc };
        for (var ordinal = 1; ordinal <= context.Selection.NodeCount; ordinal++)
        {
            var index = ordinal.ToString(CultureInfo.InvariantCulture);
            lines.Add(PeerPrefix + index + " = " + RabbitPrefix + NodePrefix + index);
        }
        var path = Path.Combine(context.DataDirectory(DirectoryName), FileName);
        File.WriteAllText(path, string.Join('\n', lines) + '\n');
        return path;
    }

    internal static void Configure(IResourceBuilder<RabbitMQServerResource> node, string name,
        IResourceBuilder<ParameterResource> cookie, string configuration)
    {
        node.WithContainerNetworkAlias(name)
            .WithEnvironment(NodeNameSetting, RabbitPrefix + name).WithEnvironment(LongNameSetting, FalseValue)
            .WithEnvironment(CookieSetting, cookie).WithEnvironment(ConfigSetting, ConfigStem)
            .WithBindMount(configuration, ConfigTarget, isReadOnly: true);
    }
}
