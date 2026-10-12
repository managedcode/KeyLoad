using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeCanonicalModelCutMounts
{
    private const string MountRoot = "/request-probes";
    private const string EnabledEnvironment = "KeyLoad__RequestCqrsProbe__Enabled";
    private const string RootEnvironment = "KeyLoad__RequestCqrsProbe__Root";
    private const string SessionEnvironment = "KeyLoad__RequestCqrsProbe__SessionId";
    private const string Enabled = "true";
    private const int SingleResource = 1;

    internal static void Configure(IDistributedApplicationTestingBuilder builder,
        NativeCanonicalModelCutFiles files, string session)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var nodes = builder.Resources.OfType<ContainerResource>()
            .Where(resource => TwoRf3MembershipProtocol.Nodes.Contains(resource.Name, StringComparer.Ordinal)).ToArray();
        if (nodes.Length != TwoRf3MembershipProtocol.NodeCount
            || TwoRf3MembershipProtocol.Nodes.Any(node => nodes.Count(resource => resource.Name == node) != SingleResource))
        { throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState); }
        foreach (var node in NativeCanonicalModelCutFiles.Nodes)
        {
            var resource = nodes.Single(value => value.Name == node);
            builder.CreateResourceBuilder(resource)
                .WithBindMount(files.Directories[node], MountRoot)
                .WithEnvironment(EnabledEnvironment, Enabled)
                .WithEnvironment(RootEnvironment, MountRoot)
                .WithEnvironment(SessionEnvironment, session);
        }
    }
}
