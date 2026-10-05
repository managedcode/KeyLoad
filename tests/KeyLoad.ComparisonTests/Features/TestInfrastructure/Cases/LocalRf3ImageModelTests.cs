using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions.Enums;

namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal sealed class LocalRf3ImageModelTests
{
    private const string Suite = "--KeyLoadTests:Suite=rf3";
    private const string WrongSuite = "--KeyLoadTests:Suite=unit";
    private const string Filter = "--KeyLoadTests:Filter=Features.ClusterReplication.Cases.LocalDevelopmentSmoke";
    private const string Enabled = "--KeyLoadTests:LocalRf3Image:Enabled=true";
    private const string Runner = "tests-rf3";
    private const string Preparation = "prepare-local-rf3-server-image";
    private const string ProvenanceEnvironment = "KEYLOAD_IMAGE_PROVENANCE";
    private const string ReferenceEnvironment = "KeyLoad__ContainerImages__Server";
    private const string ReceiptEnvironment = "KEYLOAD_LOCAL_IMAGE_RECEIPT";
    private const string NodeCommand = "node";
    private const string Script = "scripts/Features/TestInfrastructure/local-server-image.mjs";
    private const string LocalTag = "local-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string LocalReceipt = "TestResults/rf3/local-images/image-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.json";
    private static readonly string[] EmptyAmbientImageIdentity =
    [
        "--GITHUB_SHA=", "--GITHUB_ACTIONS=", "--KEYLOAD_IMAGE_RECEIPT=", "--KEYLOAD_IMAGE_PROVENANCE=",
        "--KEYLOAD_LOCAL_IMAGE_RECEIPT=", "--KEYLOAD_LOCAL_RF3_IMAGE_CHILD=", "--KeyLoad:ContainerImages:Server="
    ];

    [Test]
    public async Task ExplicitFilteredRf3WaitsForOneUniqueLocalImageReceiptWithoutStartingNodes()
    {
        await using var app = CreateApplication([Suite, Filter, Enabled, .. EmptyAmbientImageIdentity]);
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        var executables = model.Resources.OfType<ExecutableResource>().ToArray();
        await Assert.That(executables.Select(resource => resource.Name))
            .IsEquivalentTo([Runner, Preparation], CollectionOrdering.Matching);
        await Assert.That(model.Resources.OfType<ContainerResource>()).IsEmpty();

        var runner = executables.Single(resource => resource.Name == Runner);
        var preparation = executables.Single(resource => resource.Name == Preparation);
        var wait = runner.Annotations.OfType<WaitAnnotation>().Single();
        await Assert.That(ReferenceEquals(wait.Resource, preparation)).IsTrue();
        await Assert.That(wait.WaitType).IsEqualTo(WaitType.WaitForCompletion);
        await Assert.That(wait.ExitCode).IsEqualTo(0);
        await Assert.That(preparation.Command).IsEqualTo(NodeCommand);
        var preparationConfiguration = await ReadConfigurationAsync(preparation);
        await Assert.That(preparationConfiguration.Arguments.Select(argument => argument.Value)).IsEquivalentTo(
            new[] { Path.Combine(RepositoryRoot(), Script), "prepare", ReadTag(preparationConfiguration), ReadReceipt(preparationConfiguration) },
            CollectionOrdering.Matching);

        var runnerConfiguration = await ReadConfigurationAsync(runner);
        var environment = runnerConfiguration.EnvironmentVariables.ToDictionary();
        await Assert.That(environment[ProvenanceEnvironment]).IsEqualTo("local-development");
        await Assert.That(environment[ReferenceEnvironment]).IsEqualTo("keyload/local-server:" + ReadTag(preparationConfiguration));
        await Assert.That(environment[ReceiptEnvironment]).IsEqualTo(ReadReceipt(preparationConfiguration));
        await Assert.That(runnerConfiguration.Arguments.Select(argument => argument.Value))
            .Contains(Filter["--KeyLoadTests:Filter=".Length..]);
    }

    [Test]
    public async Task DefaultGitHubReceiptAndProvenanceDoNotSelectLocalImageMode()
    {
        var builder = CreateBuilder("--KEYLOAD_IMAGE_RECEIPT=TestResults/rf3/github-image.json");
        await Assert.That(TestSuiteSettings.Read(builder.Configuration)).IsNull();
    }

    [Test]
    public async Task LocalImageModeRejectsUnfilteredWrongSuiteAndMixedImageCohortsBeforeResources()
    {
        await AssertRejectedAsync(Enabled, Filter);
        await AssertRejectedAsync(Suite, Enabled);
        await AssertRejectedAsync(WrongSuite, Filter, Enabled);
        await AssertRejectedAsync(Suite, Filter, Enabled, "--GITHUB_SHA=abc");
        await AssertRejectedAsync(Suite, Filter, Enabled, "--GITHUB_ACTIONS=true");
        await AssertRejectedAsync(Suite, Filter, Enabled, "--KEYLOAD_IMAGE_RECEIPT=/tmp/receipt.json");
        await AssertRejectedAsync(Suite, Filter, Enabled, "--KeyLoad:ContainerImages:Server=registry/keyload:tag@sha256=" + new string('a', 64));
        await AssertRejectedAsync(Suite, Filter, Enabled, "--KeyLoadTests:ProtocolCohort:Enabled=true");
        await AssertRejectedAsync(Suite, Filter, Enabled, "--Benchmarks:Target=ZoneTree");
    }

    [Test]
    public async Task ChildImageModelRejectsProtocolAndComparisonSelectorsBeforeResources()
    {
        foreach (var selector in new[]
        {
            "--KeyLoadTests:ProtocolCohort:Enabled=false",
            "--KeyLoadTests:ProtocolCohort:Voters:node4=not-configured",
            "--Benchmarks:Enabled=false",
            "--Benchmarks:Target=ZoneTree",
            "--Benchmarks:NodeCount=1",
            "--Benchmarks:Scenario=PointRead",
            "--Benchmarks:Profile=scaled-100k-c16",
            "--Benchmarks:ScaleProfile=scaled-100k-c16"
        })
        {
            var builder = CreateBuilder(ChildConfiguration(selector));
            await Assert.That(() => LocalDevelopmentContainerImage.Read(builder)).Throws<InvalidOperationException>();
            await Assert.That(builder.Resources.Select(resource => resource.Name)).IsEmpty();
        }
    }

    private static string[] ChildConfiguration(string selector)
        =>
        [
            "--KEYLOAD_IMAGE_PROVENANCE=local-development",
            "--KEYLOAD_LOCAL_RF3_IMAGE_CHILD=true",
            "--KEYLOAD_LOCAL_IMAGE_RECEIPT=" + LocalReceipt,
            "--KeyLoad:ContainerImages:Server=keyload/local-server:" + LocalTag,
            selector
        ];

    private static string ReadTag(IExecutionConfigurationResult preparation)
        => preparation.Arguments.Single(argument => argument.Value?.StartsWith("local-", StringComparison.Ordinal) == true).Value!;

    private static string ReadReceipt(IExecutionConfigurationResult preparation)
        => preparation.Arguments.Single(argument => argument.Value?.StartsWith("TestResults/", StringComparison.Ordinal) == true).Value!;

    private static async Task AssertRejectedAsync(params string[] args)
    {
        var builder = CreateBuilder(args);
        await Assert.That(() => KeyLoadAppHostApplication.AddKeyLoad(builder)).Throws<InvalidOperationException>();
        await Assert.That(builder.Resources.Select(resource => resource.Name)).IsEmpty();
    }

    private static async Task<IExecutionConfigurationResult> ReadConfigurationAsync(ExecutableResource resource)
    {
        var result = await ExecutionConfigurationBuilder.Create(resource).WithEnvironmentVariablesConfig()
            .WithArgumentsConfig().BuildAsync(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
                NullLogger.Instance, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.Exception).IsNull();
        return result;
    }

    private static DistributedApplication CreateApplication(params string[] args)
    {
        var builder = CreateBuilder(args);
        KeyLoadAppHostApplication.AddKeyLoad(builder);
        return builder.Build();
    }

    private static IDistributedApplicationBuilder CreateBuilder(params string[] args)
        => DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            DisableDashboard = true,
            ProjectDirectory = Path.Combine(RepositoryRoot(), "src", "KeyLoad.AppHost"),
            Args = args
        });

    private static string RepositoryRoot()
    {
        for (var directory = AppContext.BaseDirectory; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (File.Exists(Path.Combine(directory, "KeyLoad.slnx")))
            {
                return directory;
            }
        }
        throw new InvalidOperationException("The local RF3 image model requires the source checkout.");
    }
}
