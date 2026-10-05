using KeyLoad.AppHost.Features.ClusterReplication;
using Microsoft.Extensions.Configuration;
using KeyLoad.AppHost.Hosting;

namespace KeyLoad.AppHost.Features.ClusterRouting;

/// <summary>Admits the private per-voter phase-control mount only for an ephemeral RF3 host.</summary>
internal sealed class RequestCqrsProbeProfile
{
    private const string DiscoveryCaptureVoter = "node1";

    internal const string Section = "KeyLoadTests:RequestCqrsProbe";
    private const string MountRoot = "/request-probes";
    private const string EnabledEnvironment = "KeyLoad__RequestCqrsProbe__Enabled";
    private const string RootEnvironment = "KeyLoad__RequestCqrsProbe__Root";
    private const string SessionEnvironment = "KeyLoad__RequestCqrsProbe__SessionId";
    private const string DiscoveryModeEnvironment = "KeyLoad__RequestCqrsProbe__DiscoveryCaptureMode";
    private const string MixedMode = "mixed-interface3-v1";
    private const string EnabledValue = "true";
    private const string InvalidConfiguration = "RequestCqrsProbeConfigurationInvalid";

    private readonly IReadOnlyDictionary<string, string> nodeRoots;
    private readonly string discoveryCaptureMode;

    private RequestCqrsProbeProfile(string sessionId, string discoveryCaptureMode, IReadOnlyDictionary<string, string> nodeRoots)
    { SessionId = sessionId; this.discoveryCaptureMode = discoveryCaptureMode; this.nodeRoots = nodeRoots; }

    internal string SessionId { get; }

    internal static void ValidateMode(IConfiguration configuration)
    { _ = RequestCqrsProbeProfileSettingsReader.Read(configuration); }

    internal static RequestCqrsProbeProfile? Read(IDistributedApplicationBuilder builder, string dataRoot,
        bool ephemeral, int? benchmarkNodeCount, IReadOnlyDictionary<string, RuntimeContainerImage> images)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var settings = AppHostOptionsRegistration.Get(builder).Control.Value.RequestProbe;
        if (settings is null)
        { return null; }
        if (!ephemeral || benchmarkNodeCount is not null)
        { throw new InvalidOperationException(InvalidConfiguration); }
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(images);
        ValidateImages(images, settings.DiscoveryCaptureMode);
        var nodeRoots = RequestCqrsProbeProfilePaths.Validate(settings.Root, settings.SessionId, dataRoot);
        return new(settings.SessionId, settings.DiscoveryCaptureMode, nodeRoots);
    }

    internal void Apply(IResourceBuilder<ContainerResource> resource, string node)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (!nodeRoots.TryGetValue(node, out var nodeRoot))
        { throw new InvalidOperationException(InvalidConfiguration); }
        resource.WithBindMount(nodeRoot, MountRoot)
            .WithEnvironment(EnabledEnvironment, EnabledValue)
            .WithEnvironment(RootEnvironment, MountRoot)
            .WithEnvironment(SessionEnvironment, SessionId);
        if (node == DiscoveryCaptureVoter && discoveryCaptureMode == MixedMode)
        { resource.WithEnvironment(DiscoveryModeEnvironment, MixedMode); }
    }

    private static void ValidateImages(IReadOnlyDictionary<string, RuntimeContainerImage> images, string discoveryCaptureMode)
    {
        const int IndexValue = 0;
        const int ValidateImagesIndexValue = 1;

        if (images.Count != RequestCqrsProbeProfileSettingsReader.VoterNames.Count
            || RequestCqrsProbeProfileSettingsReader.VoterNames.Any(node => !images.TryGetValue(node, out var image) || image is null))
        { throw new InvalidOperationException(InvalidConfiguration); }
        var voters = RequestCqrsProbeProfileSettingsReader.VoterNames;
        var reference = images[voters[IndexValue]].Reference;
        if (string.IsNullOrWhiteSpace(reference))
        { throw new InvalidOperationException(InvalidConfiguration); }
        var second = images[voters[ValidateImagesIndexValue]].Reference;
        var third = images[voters[2]].Reference;
        var sameImage = string.Equals(second, reference, StringComparison.Ordinal)
            && string.Equals(third, reference, StringComparison.Ordinal);
        var exactMixed = discoveryCaptureMode == MixedMode
            && !string.Equals(reference, second, StringComparison.Ordinal)
            && string.Equals(second, third, StringComparison.Ordinal);
        if (discoveryCaptureMode == MixedMode ? !exactMixed : !sameImage)
        { throw new InvalidOperationException(InvalidConfiguration); }
    }
}
