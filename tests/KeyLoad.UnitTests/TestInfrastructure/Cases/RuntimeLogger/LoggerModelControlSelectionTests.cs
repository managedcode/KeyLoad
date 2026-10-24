using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Server;
using KeyLoad.UnitTests.TestInfrastructure.Helpers;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.TestInfrastructure.Cases;

internal sealed class LoggerModelControlSelectionTests
{
    private const string RootPrefix = "keyload-c1-logger-model-";
    private const string TrueValue = "true";
    private const string FalseValue = "false";
    private const string ComparisonTargetValue = "KeyLoad";
    private const string NestedControlSetting = LoggerModelControlSetting + ":Nested";
    private const string SolutionFileName = "KeyLoad.slnx";
    private const string SourceDirectoryName = "build";
    private const string AppHostProjectName = "KeyLoad.AppHost";
    private const string MissingRepositoryMessage = "The execution AppHost model requires the source checkout.";
    private const string GuidFormat = "N";
    private const string ArgumentPrefix = "--";
    private const string ArgumentSeparator = "=";
    private const string LoggerModelControlSetting = "KeyLoadTests:LoggerModelControl";
    private const string InvalidSelectionMessage = "The C1 logger model control selection is invalid.";
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
    private const int IdentityParameterCount = 4;
    private const string BenchmarkEnabledSetting = "Benchmarks:Enabled";
    private const string ComparisonTargetSetting = "Benchmarks:Target";
    private const string NodeOne = "node1";
    private const string NodeTwo = "node2";
    private const string NodeThree = "node3";
    private static readonly string[] NodeNames = [NodeOne, NodeTwo, NodeThree];

    [Test]
    public async Task ExactSelectionBuildsTheOwnedClusterModelAndSettlesDisposal()
    {
        var root = NewRoot();
        var builder = CreateBuilder(root, includeImage: true);
        var failures = new List<Exception>();
        DistributedApplication? app = null;
        ServerFailureObserver.Observe(() => AppHostProgram.AddKeyLoad(builder), failures);
        ClusterIdentity? profile = null;
        if (failures.Count == 0)
        {
            ServerFailureObserver.Observe(() =>
            {
                app = builder.Build();
                profile = app.Services.GetRequiredService<ClusterIdentity>();
            }, failures);
        }
        if (failures.Count == 0 && app is { } built && profile is { } created)
        {
            await ServerFailureObserver.ObserveAsync(() => VerifyModelAsync(built, root, created.Incarnation), failures).ConfigureAwait(false);
        }
        if (app is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => app.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        if (Directory.Exists(root))
        {
            ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    [Test]
    public async Task InvalidControlAndWorkloadSelectionsRejectBeforeResourcesOrFiles()
    {
        foreach (var selection in RejectedSelections())
        {
            var root = NewRoot();
            await LoggerModelControlRejectionFlow.AssertRejectedAsync(root, selection.Name,
                selection.Arguments, selection.IncludeImage, selection.ExpectedMessage, CreateBuilder).ConfigureAwait(false);
        }
    }

    private static async Task VerifyModelAsync(DistributedApplication app, string root, Guid incarnation)
    {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var nodes = model.Resources.OfType<ProjectResource>().ToArray();
        await Assert.That(nodes.Select(node => node.Name).ToArray()).IsEquivalentTo(NodeNames, CollectionOrdering.Matching);
        await Assert.That(model.Resources.OfType<ContainerResource>()).IsEmpty();
        await Assert.That(model.Resources.OfType<ParameterResource>().Count()).IsEqualTo(IdentityParameterCount);
        await Assert.That(Directory.GetFiles(root)).IsEmpty();
        var parameter = model.Resources.OfType<ParameterResource>().Single(value => value.Name == "incarnation");
        await Assert.That(await parameter.GetValueAsync(TestContext.Current!.Execution.CancellationToken)).IsEqualTo(incarnation.ToString("D"));
        foreach (var node in nodes)
        {
            await VerifyNodeAsync(node, root).ConfigureAwait(false);
        }
    }

    private static async Task VerifyNodeAsync(ProjectResource node, string root)
    {
        await Assert.That(node.GetProjectMetadata().ProjectPath).EndsWith("KeyLoad.csproj");
        await Assert.That(Directory.Exists(Path.Combine(root, node.Name))).IsTrue();
        var endpoint = node.Annotations.OfType<EndpointAnnotation>().Single(value => value.Name == "http");
        await Assert.That(endpoint.Port).IsNull();
        await Assert.That(endpoint.IsProxied).IsFalse();
    }

    private static (string Name, string[] Arguments, bool IncludeImage, string ExpectedMessage)[] RejectedSelections()
    {
        return
        [
            (nameof(FalseValue), [Argument(LoggerModelControlSetting, FalseValue)], true, InvalidSelectionMessage),
            (nameof(LoggerModelControlSetting), [Argument(LoggerModelControlSetting, string.Empty)], true, InvalidSelectionMessage),
            (nameof(NestedControlSetting), [Argument(NestedControlSetting, TrueValue)], true, InvalidSelectionMessage),
            (nameof(BenchmarkEnabledSetting), [Argument(BenchmarkEnabledSetting, TrueValue)], true, InvalidSelectionMessage),
            (nameof(ComparisonTargetSetting), [Argument(ComparisonTargetSetting, ComparisonTargetValue)], true, InvalidSelectionMessage),
            (nameof(EphemeralSetting), [Argument(EphemeralSetting, FalseValue)], true, InvalidSelectionMessage),
            (nameof(DataRootSetting), [Argument(DataRootSetting, string.Empty)], true, AppHostStartupOptions.ValidationMessage)
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
        {
            arguments.Add(Argument(ImageSetting, ImageReference));
        }
        arguments.AddRange(overrides);
        return DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            DisableDashboard = true,
            ProjectDirectory = Path.Combine(RepositoryRoot(), SourceDirectoryName, AppHostProjectName),
            Args = [.. arguments]
        });
    }

    private static string Argument(string setting, string value) => ArgumentPrefix + setting + ArgumentSeparator + value;

    private static string NewRoot() => Path.Combine(AppContext.BaseDirectory, RootPrefix + Guid.NewGuid().ToString(GuidFormat));

    private static string RepositoryRoot()
    {
        for (var directory = AppContext.BaseDirectory; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (File.Exists(Path.Combine(directory, SolutionFileName)))
            {
                return directory;
            }
        }
        throw new InvalidOperationException(MissingRepositoryMessage);
    }
}
