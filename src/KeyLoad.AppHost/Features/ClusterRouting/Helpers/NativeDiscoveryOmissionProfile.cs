using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.AppHost.Features.ClusterRouting;

[ConfigurationBinding]
internal sealed class NativeDiscoveryOmissionProfile
{
    internal const string Section = "KeyLoadTests:NativeDiscoveryOmission";
    private const string Enabled = "Enabled";
    private const string Root = "Root";
    private const string SessionId = "SessionId";
    private const string Mount = "/native-discovery-omission";
    private const string EnabledEnvironment = "KeyLoad__NativeDiscoveryOmission__Enabled";
    private const string RootEnvironment = "KeyLoad__NativeDiscoveryOmission__Root";
    private const string SessionEnvironment = "KeyLoad__NativeDiscoveryOmission__SessionId";
    private const string True = "true";
    private const string InvalidConfiguration = "NativeDiscoveryOmissionProfileInvalid";
    private const int MaximumFields = 3;
    private const int ExcessField = 1;
    private const int NoFields = 0;
    private const string IdentityFormat = "N";
    private readonly RequestCqrsProbeProfileSettings settings;
    private NativeDiscoveryOmissionProfile(RequestCqrsProbeProfileSettings settings) => this.settings = settings;
    internal static RequestCqrsProbeProfileSettings? ReadSettings(IConfiguration configuration, AppHostControlOptions control)
    {
        var section = configuration.GetSection(Section);
        var fields = section.GetChildren().Take(MaximumFields + ExcessField).ToArray();
        if (section.Value is null && fields.Length == NoFields)
        { return null; }
        if (section.Value is not null || fields.Length > MaximumFields
            || fields.Any(field => field.Key is not (Enabled or Root or SessionId)
                || field.Value is null || field.GetChildren().Take(ExcessField).Any())
            || section[Enabled] != True || !Guid.TryParseExact(section[SessionId], IdentityFormat, out var id)
            || id == Guid.Empty || id.ToString(IdentityFormat) != section[SessionId]
            || string.IsNullOrWhiteSpace(section[Root]) || !control.TwoRf3 || !control.Ephemeral
            || !control.ProtectedDocumentMovement || !control.RemoteDocumentReads || !control.RemotePartitionQueries
            || control.RequestProbe is null || control.Tests is not null || control.ProtocolCohortEnabled
            || control.ProtocolCohortConfigured || control.BenchmarksEnabled || control.ComparisonSelectorsPresent
            || control.TargetSelected || control.ScaleSelected || control.LoggerModelControl)
        { throw Invalid(); }
        return new(section[Root]!, section[SessionId]!);
    }
    internal static NativeDiscoveryOmissionProfile? Create(IDistributedApplicationBuilder builder, string dataRoot,
        RuntimeContainerImage? image, LocalDevelopmentContainerImage? local)
    {
        var selected = AppHostOptionsRegistration.Get(builder).Control.Value.NativeDiscoveryOmission;
        if (selected is null)
        { return null; }
        if (image is null || local is not null)
        { throw Invalid(); }
        _ = RequestCqrsProbeProfilePaths.Validate(selected.Root, selected.SessionId, dataRoot,
            AppHostOptionsRegistration.Get(builder).RequestProbeFiles);
        return new(selected);
    }
    internal void Apply(IReadOnlyList<IResourceBuilder<ContainerResource>> resources)
    {
        for (var index = NoFields; index < TwoRf3ProfileProtocol.MembersPerGroup; index++)
        {
            resources[index].WithBindMount(settings.Root, Mount)
                .WithEnvironment(EnabledEnvironment, True).WithEnvironment(RootEnvironment, Mount)
                .WithEnvironment(SessionEnvironment, settings.SessionId);
        }
    }
    private static InvalidOperationException Invalid() => new(InvalidConfiguration);
}
