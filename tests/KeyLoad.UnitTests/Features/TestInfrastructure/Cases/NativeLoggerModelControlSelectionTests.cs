using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.TestInfrastructure.Helpers;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.TestInfrastructure.Cases;

internal sealed class NativeLoggerModelControlSelectionTests
{
    private const string RootPrefix = "keyload-c1-logger-model-";
    private const string TrueValue = "true";
    private const string FalseValue = "false";
    private const string ComparisonTargetValue = "KeyLoad";
    private const string NestedControlSetting = LoggerModelControlSetting + ":Nested";
    private const string ContainerNamePrefix = "keyload-";
    private const string ImageDigestMarker = "@sha256:";
    private const string SolutionFileName = "KeyLoad.slnx";
    private const string SourceDirectoryName = "src";
    private const string AppHostProjectName = "KeyLoad.AppHost";
    private const string MissingRepositoryMessage = "The native AppHost model requires the source checkout.";
    private const string GuidFormat = "N";
    private const string ArgumentPrefix = "--";
    private const string ArgumentSeparator = "=";
    private const string LoggerModelControlSetting = "KeyLoadTests:LoggerModelControl";
    private const string InvalidSelectionMessage = "The C1 logger model control selection is invalid.";
    private const string InvalidLocalImageMessage = "Local RF3 image mode configuration is invalid.";
    private const string EphemeralSetting = "KeyLoad:Ephemeral";
    private const string DataRootSetting = "KeyLoad:DataRoot";
    private const string FirstPublicPortSetting = "KeyLoad:FirstPublicPort";
    private const string ContainerUserSetting = "KeyLoad:ContainerUser";
    private const string ContainerUser = "1000:1000";
    private const string FirstPublicPort = "5411";
    private const string ImageSetting = "KeyLoad:ContainerImages:Server";
    private const string ImageReference = ImageRepository + ":" + ImageTag + "@sha256:" + ImageDigest;
    private const string ImageRepository = "ghcr.io/managedcode/keyload";
    private const string ImageTag = "current";
    private const string ImageDigest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DataMountTarget = "/data";
    private const int IdentityParameterCount = 4;
    private const string SuiteSetting = "KeyLoadTests:Suite";
    private const string UnitSuite = "unit";
    private const string Rf3Suite = "rf3";
    private const string FilterSetting = "KeyLoadTests:Filter";
    private const string NativeFilter = "/*/*/ActualCase/*";
    private const string BenchmarkEnabledSetting = "Benchmarks:Enabled";
    private const string ComparisonTargetSetting = "Benchmarks:Target";
    private const string TwoRf3Setting = "KeyLoadTests:ClusterRouting:Profile";
    private const string TwoRf3Profile = "two-rf3";
    private const string RequestProbeEnabledSetting = "KeyLoadTests:RequestCqrsProbe:Enabled";
    private const string RequestProbeRootSetting = "KeyLoadTests:RequestCqrsProbe:Root";
    private const string RequestProbeSessionSetting = "KeyLoadTests:RequestCqrsProbe:SessionId";
    private const string RequestProbeSession = "0123456789abcdef0123456789abcdef";
    private const string ProtocolEnabledSetting = "KeyLoadTests:ProtocolCohort:Enabled";
    private const string ProtocolVoterPrefix = "KeyLoadTests:ProtocolCohort:Voters:";
    private const string LocalImageEnabledSetting = "KeyLoadTests:LocalRf3Image:Enabled";
    private const string GithubActionsSetting = "GITHUB_ACTIONS";
    private const string NodeOne = "node1";
    private const string NodeTwo = "node2";
    private const string NodeThree = "node3";
    private static readonly string[] NodeNames = [NodeOne, NodeTwo, NodeThree];

    [Test]
    public async Task ExactSelectionBuildsTheOwnedRf3ModelAndSettlesDisposal()
    {
        var root = NewRoot();
        var builder = CreateBuilder(root, includeImage: true);
        var failures = new List<Exception>();
        DistributedApplication? app = null;
        ServerFailureObserver.Observe(() => KeyLoadAppHostApplication.AddKeyLoad(builder), failures);
        LocalProfile? profile = null;
        if (failures.Count == 0)
        { ServerFailureObserver.Observe(() => profile = ReadCreatedProfile(root, builder), failures); }
        if (failures.Count == 0)
        { ServerFailureObserver.Observe(() => app = builder.Build(), failures); }
        if (failures.Count == 0 && app is { } built && profile is { } created)
        { await ServerFailureObserver.ObserveAsync(() => VerifyModelAsync(built, root, created.Incarnation), failures).ConfigureAwait(false); }
        if (app is not null)
        { await ServerFailureObserver.ObserveAsync(() => app.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (Directory.Exists(root))
        { ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    [Test]
    public async Task InvalidControlAndWorkloadSelectionsRejectBeforeResourcesOrFiles()
    {
        foreach (var selection in RejectedSelections())
        {
            var root = NewRoot();
            await NativeLoggerModelControlRejectionFlow.AssertRejectedAsync(root, selection.Name,
                selection.Arguments, selection.IncludeImage, selection.ExpectedMessage, CreateBuilder).ConfigureAwait(false);
        }
    }

    private static async Task VerifyModelAsync(DistributedApplication app, string root, Guid incarnation)
    {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var nodes = model.Resources.OfType<ContainerResource>().ToArray();
        await Assert.That(nodes.Select(node => node.Name).ToArray()).IsEquivalentTo(NodeNames, CollectionOrdering.Matching);
        await Assert.That(model.Resources.OfType<ExecutableResource>()).IsEmpty();
        await Assert.That(model.Resources.OfType<ParameterResource>().Count()).IsEqualTo(IdentityParameterCount);
        await Assert.That(File.Exists(Path.Combine(root, ClusterProfileStore.ProfileName))).IsTrue();
        foreach (var node in nodes)
        { await VerifyNodeAsync(node, root, incarnation).ConfigureAwait(false); }
    }

    private static async Task VerifyNodeAsync(ContainerResource node, string root, Guid incarnation)
    {
        var expectedContainerName = ContainerNamePrefix + incarnation.ToString(GuidFormat) + "-" + node.Name;
        await Assert.That(node.Annotations.OfType<ContainerNameAnnotation>().Single().Name).IsEqualTo(expectedContainerName);
        var mount = node.Annotations.OfType<ContainerMountAnnotation>().Single();
        await Assert.That(mount.Source).IsEqualTo(Path.Combine(root, node.Name));
        await Assert.That(mount.Target).IsEqualTo(DataMountTarget);
        await Assert.That(mount.Type).IsEqualTo(ContainerMountType.BindMount);
        await Assert.That(mount.IsReadOnly).IsFalse();
        await Assert.That(Directory.Exists(mount.Source)).IsTrue();
        await VerifyImageAsync(node);
        await NativeLoggerModelEndpointAssertions.VerifyAsync(node);
    }

    private static async Task VerifyImageAsync(ContainerResource node)
    {
        var image = node.Annotations.OfType<ContainerImageAnnotation>().Single();
        await Assert.That(image.Image).IsEqualTo(ImageRepository);
        await Assert.That(image.Tag).IsNull();
        await Assert.That(image.SHA256).IsEqualTo(ImageDigest);
        await Assert.That(node.TryGetContainerImageName(out var actual)).IsTrue();
        await Assert.That(actual).IsEqualTo(ImageRepository + ImageDigestMarker + ImageDigest);
    }

    private static (string Name, string[] Arguments, bool IncludeImage, string ExpectedMessage)[] RejectedSelections()
    {
        var probeRoot = Path.Combine(Path.GetTempPath(), RootPrefix + RequestProbeSession);
        return
        [
            (nameof(FalseValue), [Argument(LoggerModelControlSetting, FalseValue)], true, InvalidSelectionMessage),
            (nameof(LoggerModelControlSetting), [Argument(LoggerModelControlSetting, string.Empty)], true, InvalidSelectionMessage),
            (nameof(NestedControlSetting), [Argument(NestedControlSetting, TrueValue)], true, InvalidSelectionMessage),
            (nameof(UnitSuite), [Argument(SuiteSetting, UnitSuite)], true, InvalidSelectionMessage),
            (nameof(BenchmarkEnabledSetting), [Argument(BenchmarkEnabledSetting, TrueValue)], true, InvalidSelectionMessage),
            (nameof(ComparisonTargetSetting), [Argument(ComparisonTargetSetting, ComparisonTargetValue)], true, InvalidSelectionMessage),
            (nameof(TwoRf3Profile), [Argument(TwoRf3Setting, TwoRf3Profile)], true, InvalidSelectionMessage),
            (nameof(RequestProbeEnabledSetting), [Argument(RequestProbeEnabledSetting, TrueValue),
                Argument(RequestProbeRootSetting, probeRoot), Argument(RequestProbeSessionSetting, RequestProbeSession)], true, InvalidSelectionMessage),
            (nameof(ProtocolEnabledSetting), [Argument(ProtocolEnabledSetting, TrueValue),
                Argument(ProtocolVoterPrefix + NodeOne, ImageReference), Argument(ProtocolVoterPrefix + NodeTwo, ImageReference),
                Argument(ProtocolVoterPrefix + NodeThree, ImageReference)], true, InvalidSelectionMessage),
            (nameof(LocalImageEnabledSetting), [Argument(SuiteSetting, Rf3Suite), Argument(FilterSetting, NativeFilter),
                Argument(LocalImageEnabledSetting, TrueValue), Argument(ImageSetting, string.Empty),
                Argument(GithubActionsSetting, TrueValue)], false, InvalidLocalImageMessage),
            (nameof(EphemeralSetting), [Argument(EphemeralSetting, FalseValue)], true, InvalidSelectionMessage),
            (nameof(DataRootSetting), [Argument(DataRootSetting, string.Empty)], true, InvalidSelectionMessage)
        ];
    }

    internal static IDistributedApplicationBuilder CreateBuilder(string root, bool includeImage,
        params string[] overrides)
    {
        var arguments = new List<string>
        {
            Argument(LoggerModelControlSetting, TrueValue), Argument(EphemeralSetting, TrueValue),
            Argument(DataRootSetting, root), Argument(FirstPublicPortSetting, FirstPublicPort),
            Argument(ContainerUserSetting, ContainerUser)
        };
        if (includeImage)
        { arguments.Add(Argument(ImageSetting, ImageReference)); }
        arguments.AddRange(overrides);
        return DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            DisableDashboard = true,
            ProjectDirectory = Path.Combine(RepositoryRoot(), SourceDirectoryName, AppHostProjectName),
            Args = [.. arguments]
        });
    }

    private static LocalProfile ReadCreatedProfile(string root, IDistributedApplicationBuilder builder)
    {
        var options = AppHostOptionsRegistration.Get(builder).Profile;
        var path = Path.Combine(root, ClusterProfileStore.ProfileName);
        return ClusterProfileStore.DeserializeCurrent(ClusterProfileStore.ReadBoundedBytes(path, options), options);
    }

    private static string Argument(string setting, string value) => ArgumentPrefix + setting + ArgumentSeparator + value;

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), RootPrefix + Guid.NewGuid().ToString(GuidFormat));

    private static string RepositoryRoot()
    {
        for (var directory = AppContext.BaseDirectory; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (File.Exists(Path.Combine(directory, SolutionFileName)))
            { return directory; }
        }
        throw new InvalidOperationException(MissingRepositoryMessage);
    }
}
