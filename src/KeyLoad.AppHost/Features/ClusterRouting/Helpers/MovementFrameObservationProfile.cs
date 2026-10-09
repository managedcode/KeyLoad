using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Hosting;

namespace KeyLoad.AppHost.Features.ClusterRouting;

internal sealed class MovementFrameObservationProfile(string sessionId, IReadOnlyDictionary<string, string> nodeRoots)
{
    internal static MovementFrameObservationProfile? Create(IDistributedApplicationBuilder builder, string dataRoot,
        RuntimeContainerImage? image, LocalDevelopmentContainerImage? local)
    {
        var options = AppHostOptionsRegistration.Get(builder);
        var settings = options.Control.Value.MovementFrameObservation;
        if (settings is null)
        { return null; }
        if (image is null || local is not null || string.IsNullOrWhiteSpace(image.Reference))
        { throw Invalid(); }
        var roots = RequestCqrsProbeProfilePaths.Validate(settings.Root, settings.SessionId, dataRoot, options.RequestProbeFiles,
            MovementFrameObservationProfileProtocol.Voters, MovementFrameObservationProfileProtocol.OwnerVersion,
            MovementFrameObservationProfileProtocol.OwnerKind);
        return new(settings.SessionId, roots);
    }

    internal void ApplyReceivers(IReadOnlyList<IResourceBuilder<ContainerResource>> resources, IReadOnlyList<string> nodes)
    {
        if (resources.Count != TwoRf3ProfileProtocol.TotalNodes || nodes.Count != TwoRf3ProfileProtocol.TotalNodes)
        { throw Invalid(); }
        for (var index = TwoRf3ProfileProtocol.MembersPerGroup; index < TwoRf3ProfileProtocol.TotalNodes; index++)
        { Apply(resources[index], nodes[index]); }
    }

    private void Apply(IResourceBuilder<ContainerResource> resource, string node)
    {
        if (!nodeRoots.TryGetValue(node, out var root))
        { throw Invalid(); }
        resource.WithBindMount(root, MovementFrameObservationProfileProtocol.Root)
            .WithEnvironment(MovementFrameObservationProfileProtocol.EnabledEnvironment, MovementFrameObservationProfileProtocol.EnabledValue)
            .WithEnvironment(MovementFrameObservationProfileProtocol.RootEnvironment, MovementFrameObservationProfileProtocol.Root)
            .WithEnvironment(MovementFrameObservationProfileProtocol.SessionEnvironment, sessionId);
    }
    private static InvalidOperationException Invalid() => new(MovementFrameObservationProfileProtocol.Invalid);
}
