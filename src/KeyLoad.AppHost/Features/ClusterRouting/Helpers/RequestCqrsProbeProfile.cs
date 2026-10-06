using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.ClusterRouting;

/// <summary>Admits the private per-voter phase-control mount only for an ephemeral RF3 host.</summary>
internal sealed class RequestCqrsProbeProfile
{
    private const int ThirdVoterIndex = 2;

    internal const string Section = "KeyLoadTests:RequestCqrsProbe";
    private const string MountRoot = "/request-probes";
    private const string EnabledEnvironment = "KeyLoad__RequestCqrsProbe__Enabled";
    private const string RootEnvironment = "KeyLoad__RequestCqrsProbe__Root";
    private const string SessionEnvironment = "KeyLoad__RequestCqrsProbe__SessionId";
    private const string EnabledValue = "true";
    private const string InvalidConfiguration = "RequestCqrsProbeConfigurationInvalid";

    private readonly IReadOnlyDictionary<string, string> nodeRoots;

    private RequestCqrsProbeProfile(string sessionId, IReadOnlyDictionary<string, string> nodeRoots)
    { SessionId = sessionId; this.nodeRoots = nodeRoots; }

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
        ValidateImages(images);
        var nodeRoots = RequestCqrsProbeProfilePaths.Validate(settings.Root, settings.SessionId, dataRoot, AppHostOptionsRegistration.Get(builder).RequestProbeFiles);
        return new(settings.SessionId, nodeRoots);
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
    }

    private static void ValidateImages(IReadOnlyDictionary<string, RuntimeContainerImage> images)
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
        var third = images[voters[ThirdVoterIndex]].Reference;
        if (!string.Equals(second, reference, StringComparison.Ordinal)
            || !string.Equals(third, reference, StringComparison.Ordinal))
        { throw new InvalidOperationException(InvalidConfiguration); }
    }
}
