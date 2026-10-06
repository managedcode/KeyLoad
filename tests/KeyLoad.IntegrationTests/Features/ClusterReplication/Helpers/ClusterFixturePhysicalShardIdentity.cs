using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.AppHost.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ClusterFixturePhysicalShardIdentity
{
    internal static LocalProfile ReadProfile(string root)
    {
        var path = Path.Combine(root, ClusterFixtureProtocol.ProfileFileName);
        var execution = IntegrationProfileOptions.Execution();
        return global::ClusterProfileStore.DeserializeCurrent(global::ClusterProfileStore.ReadBoundedBytes(path, execution), execution);
    }

    internal static void OverrideNode(IDistributedApplicationTestingBuilder builder, string? nodeName, Guid identity)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (nodeName is null)
        { return; }
        if (!ClusterFixtureProtocol.IsNodeName(nodeName) || identity == Guid.Empty)
        { throw new ArgumentException("The physical-shard override must identify one RF3 voter and a nonempty identity."); }
        var node = builder.CreateResourceBuilder(builder.Resources.OfType<ContainerResource>()
            .Single(resource => resource.Name == nodeName));
        var value = identity.ToString("D", System.Globalization.CultureInfo.InvariantCulture);
        node.WithEnvironment(context => context.EnvironmentVariables[ClusterFixtureProtocol.PhysicalShardIdSetting] = value);
    }
}
