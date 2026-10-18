using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure.Execution;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions.Enums;

namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal sealed class WholeClassClusterPreparationModelTests
{
    private const string Suite = "--KeyLoadTests:Suite=cluster";
    private const string FilterPrefix = "--KeyLoadTests:Filter=";
    private const string Enabled = "--KeyLoadTests:LocalClusterImage:Enabled=true";
    private const string Runner = "tests-cluster";
    private const string Preparation = "prepare-local-cluster-server-image";
    private const string Provenance = "KEYLOAD_IMAGE_PROVENANCE";
    private const string Image = "KeyLoad__ContainerImages__Server";
    private const string Receipt = "KEYLOAD_LOCAL_IMAGE_RECEIPT";
    private const string TagPrefix = "local-";
    private const string ReceiptPrefix = "TestResults/cluster/local-images/image-";
    private const string JsonSuffix = ".json";
    private const string ImagePrefix = "keyload/local-server:";
    private const string PreparationMode = "prepare";
    private const string Node = "node";
    private const string Solution = "KeyLoad.slnx";
    private const string AppHostDirectory = "build/KeyLoad.AppHost";
    private const string Script = "scripts/Features/TestInfrastructure/local-server-image.mjs";
    private const int SuccessExit = 0;
    private const string MaximumParallelTests = "--maximum-parallel-tests";
    private const string NativeParallelism = "50";
    private static readonly string[] ForeignIdentity =
    [
        "--GITHUB_SHA=foreign", "--KEYLOAD_IMAGE_RECEIPT=foreign", "--KEYLOAD_IMAGE_PROVENANCE=foreign",
        "--KEYLOAD_LOCAL_IMAGE_RECEIPT=foreign", "--Benchmarks:Target=ZoneTree", "--KeyLoadTests:ProtocolCohort:Enabled=true"
    ];
    private static readonly string[] EmptyIdentity =
    [
        "--GITHUB_SHA=", "--GITHUB_ACTIONS=", "--KEYLOAD_IMAGE_RECEIPT=", "--KEYLOAD_IMAGE_PROVENANCE=",
        "--KEYLOAD_LOCAL_IMAGE_RECEIPT=", "--KEYLOAD_LOCAL_CLUSTER_IMAGE_CHILD=", "--KeyLoad:ContainerImages:Server="
    ];

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    public async Task ExactWholeClassOwnsUniqueImagePrerequisiteAndUnchangedRunnerDependencyAcrossDisposedModels(int selection)
    {
        var filter = WholeClassClusterAdmissionFilters.For(selection);
        await RefusedAsync(filter);
        var first = await WaveAsync(filter);
        await RefusedAsync(filter);
        var second = await WaveAsync(filter);
        await Assert.That(second).IsNotEqualTo(first);
    }

    private static async Task<string> WaveAsync(string filter)
    {
        var failures = new List<Exception>();
        string? retained = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var app = Create(filter);
            await ServerFailureObserver.ObserveAsync(async () => retained = await ObserveAsync(app, filter), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return retained ?? throw new InvalidOperationException(Preparation);
    }

    private static async Task<string> ObserveAsync(DistributedApplication app, string filter)
    {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        await Assert.That(model.Resources.Select(resource => resource.Name))
            .IsEquivalentTo([Runner, Preparation], CollectionOrdering.Matching);
        await Assert.That(model.Resources.OfType<ContainerResource>()).IsEmpty();
        var runner = model.Resources.OfType<ExecutableResource>().Single(resource => resource.Name == Runner);
        var preparation = model.Resources.OfType<ExecutableResource>().Single(resource => resource.Name == Preparation);
        var wait = runner.Annotations.OfType<WaitAnnotation>().Single();
        await Assert.That(ReferenceEquals(wait.Resource, preparation)).IsTrue();
        await Assert.That(wait.WaitType).IsEqualTo(WaitType.WaitForCompletion);
        await Assert.That(wait.ExitCode).IsEqualTo(SuccessExit);
        await Assert.That(preparation.Command).IsEqualTo(Node);
        var prepared = await ConfigurationAsync(preparation);
        var tag = prepared.Arguments.Single(argument => argument.Value?.StartsWith(TagPrefix, StringComparison.Ordinal) == true).Value!;
        var receipt = ReceiptPrefix + tag[TagPrefix.Length..] + JsonSuffix;
        await Assert.That(prepared.Arguments.Select(argument => argument.Value))
            .IsEquivalentTo([Path.Combine(Root(), Script), PreparationMode, tag, receipt], CollectionOrdering.Matching);
        var configured = await ConfigurationAsync(runner);
        var environment = configured.EnvironmentVariables.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        await Assert.That(environment[Provenance]).IsEqualTo(LocalClusterImageExecution.Provenance);
        await Assert.That(environment[Image]).IsEqualTo(ImagePrefix + tag);
        await Assert.That(environment[Receipt]).IsEqualTo(receipt);
        var arguments = configured.Arguments.Select(argument => argument.Value).ToArray();
        await Assert.That(arguments).Contains(filter);
        await Assert.That(arguments).Contains(MaximumParallelTests);
        await Assert.That(arguments[Array.IndexOf(arguments, MaximumParallelTests) + 1]).IsEqualTo(NativeParallelism);
        return tag;
    }

    private static async Task<IExecutionConfigurationResult> ConfigurationAsync(ExecutableResource resource)
    {
        var result = await ExecutionConfigurationBuilder.Create(resource).WithEnvironmentVariablesConfig().WithArgumentsConfig()
            .BuildAsync(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
                NullLogger.Instance, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.Exception).IsNull();
        return result;
    }

    private static async Task RefusedAsync(string filter)
    {
        foreach (var argument in ForeignIdentity)
        {
            var builder = CreateBuilder(filter, argument);
            await Assert.That(() => KeyLoadResources.AddKeyLoad(builder)).Throws<InvalidOperationException>();
            await Assert.That(builder.Resources.Select(resource => resource.Name)).IsEmpty();
        }
    }

    private static DistributedApplication Create(string filter)
    {
        var builder = CreateBuilder(filter);
        KeyLoadResources.AddKeyLoad(builder);
        return builder.Build();
    }

    private static IDistributedApplicationBuilder CreateBuilder(string filter, params string[] extra)
        => DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            DisableDashboard = true,
            ProjectDirectory = Path.Combine(Root(), AppHostDirectory),
            Args = [Suite, FilterPrefix + filter, Enabled, .. EmptyIdentity, .. extra],
        });

    private static string Root()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, Solution)))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException(Solution);
    }
}
