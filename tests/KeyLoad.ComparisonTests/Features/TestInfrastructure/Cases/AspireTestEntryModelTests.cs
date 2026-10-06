using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TUnit.Assertions.Enums;

namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal sealed class AspireTestEntryModelTests
{
    private const string SuiteEnvironment = "KeyLoadTests__Suite";
    private const string ScalarRuntimeEnvironment = "DOTNET_EnableHWIntrinsic";
    private const string MaximumParallelTestsArgument = "--maximum-parallel-tests";
    private const string DefaultMaximumParallelTests = "8";
    private const string FilterArgument = "--KeyLoadTests:Filter=Suite.Filter";
    private static readonly (string Suite, string Project)[] Suites =
    [
        ("analyzers", "KeyLoad.Analyzers.Tests"),
        ("unit", "KeyLoad.UnitTests"),
        ("unit-scalar", "KeyLoad.UnitTests"),
        ("recovery", "KeyLoad.RecoveryTests"),
        ("rf3", "KeyLoad.IntegrationTests"),
        ("comparison", "KeyLoad.ComparisonTests"),
        ("site", "KeyLoad.SiteTests")
    ];

    [Test]
    public async Task EveryClosedSuiteHasOneNativeRunnerAndOriginalTUnitArguments()
    {
        foreach (var (suite, project) in Suites)
        {
            var builder = CreateBuilder($"--KeyLoadTests:Suite={suite}");
            KeyLoadAppHostApplication.AddKeyLoad(builder);
            var app = builder.Build();
            try
            {
                var model = app.Services.GetRequiredService<DistributedApplicationModel>();
                var executables = model.Resources.OfType<ExecutableResource>().ToArray();
                var expectedResources = new[] { $"tests-{suite}" };
                await Assert.That(executables.Select(resource => resource.Name)).IsEquivalentTo(expectedResources);
                await Assert.That(model.Resources.OfType<ContainerResource>().Where(IsRf3Node)).IsEmpty();
                await Assert.That(model.Resources.Count).IsEqualTo(expectedResources.Length);

                var configuration = await ReadConfigurationAsync(executables.Single(resource => resource.Name == $"tests-{suite}"));
                var repository = RepositoryRoot();
                await Assert.That(configuration.Arguments.Select(argument => argument.Value)).IsEquivalentTo(
                new[]
                {
                    "test", "--project", Path.Combine(repository, "tests", project), "--no-build", "--no-restore",
                    "--configuration", "Release", "--results-directory", Path.Combine(repository, "TestResults", suite),
                    MaximumParallelTestsArgument, DefaultMaximumParallelTests
                }, CollectionOrdering.Matching);
                var environment = configuration.EnvironmentVariables.ToDictionary();
                await Assert.That(environment[SuiteEnvironment]).IsEqualTo("");
                await Assert.That(environment.ContainsKey(ScalarRuntimeEnvironment)).IsEqualTo(suite == "unit-scalar");
                if (suite == "unit-scalar")
                {
                    await Assert.That(environment[ScalarRuntimeEnvironment]).IsEqualTo("0");
                }
            }
            finally
            {
                await app.DisposeAsync();
            }
        }
    }

    [Test]
    public async Task FilterIsOneNativeArgumentAndIsBoundedBeforeResourcesAreAdded()
    {
        var builder = CreateBuilder("--KeyLoadTests:Suite=unit", FilterArgument);
        KeyLoadAppHostApplication.AddKeyLoad(builder);
        var app = builder.Build();
        try
        {
            var runner = app.Services.GetRequiredService<DistributedApplicationModel>()
                .Resources.OfType<ExecutableResource>().Single();
            var configuration = await ReadConfigurationAsync(runner);
            await Assert.That(configuration.Arguments.Select(argument => argument.Value)).IsEquivalentTo(
            new[]
            {
                "test", "--project", Path.Combine(RepositoryRoot(), "tests", "KeyLoad.UnitTests"), "--no-build",
                "--no-restore", "--configuration", "Release", "--results-directory",
                Path.Combine(RepositoryRoot(), "TestResults", "unit"), MaximumParallelTestsArgument,
                DefaultMaximumParallelTests, "--treenode-filter", "Suite.Filter"
            }, CollectionOrdering.Matching);
        }
        finally
        {
            await app.DisposeAsync();
        }

        await AssertRejectedBeforeResourcesAsync("--KeyLoadTests:Suite=unknown");
        await AssertRejectedBeforeResourcesAsync("--KeyLoadTests:Suite=unit",
            "--KeyLoadTests:Filter=" + new string('x', 4_097));
        await AssertRejectedBeforeResourcesAsync("--KeyLoadTests:Suite=unit", "--Benchmarks:Enabled=true");
        await AssertRejectedBeforeResourcesAsync("--KeyLoadTests:Suite=unit", "--Benchmarks:Target=ZoneTree");
    }

    [Test]
    public async Task ConfiguredParallelismUsesTheNativeArgumentAndInclusiveBoundaries()
    {
        foreach (var parallelism in new[] { "1", "64" })
        {
            var builder = CreateBuilder("--KeyLoadTests:Suite=unit",
                "--KeyLoadTests:Execution:MaximumParallelTests=" + parallelism);
            KeyLoadAppHostApplication.AddKeyLoad(builder);
            var app = builder.Build();
            try
            {
                var runner = app.Services.GetRequiredService<DistributedApplicationModel>()
                    .Resources.OfType<ExecutableResource>().Single(resource => resource.Name == "tests-unit");
                var configuration = await ReadConfigurationAsync(runner);
                var arguments = configuration.Arguments.Select(argument => argument.Value).ToArray();
                var argumentIndex = Array.IndexOf(arguments, MaximumParallelTestsArgument);
                await Assert.That(argumentIndex >= 0).IsTrue();
                await Assert.That(arguments[argumentIndex + 1]).IsEqualTo(parallelism);
            }
            finally
            {
                await app.DisposeAsync();
            }
        }
    }

    [Test]
    public async Task InvalidParallelismIsRejectedBeforeResourcesAreAdded()
    {
        foreach (var parallelism in new[] { "0", "-1", "65" })
        {
            var builder = CreateBuilder("--KeyLoadTests:Suite=unit",
                "--KeyLoadTests:Execution:MaximumParallelTests=" + parallelism);
            await Assert.That(() => KeyLoadAppHostApplication.AddKeyLoad(builder)).Throws<OptionsValidationException>();
            await Assert.That(builder.Resources.Select(resource => resource.Name)).IsEmpty();
        }
        await AssertRejectedBeforeResourcesAsync("--KeyLoadTests:Suite=unit",
            "--KeyLoadTests:Execution:MaximumParallelTests=not-an-integer");
    }

    private static async Task AssertRejectedBeforeResourcesAsync(params string[] args)
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

    private static IDistributedApplicationBuilder CreateBuilder(params string[] args)
        => DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            DisableDashboard = true,
            ProjectDirectory = Path.Combine(RepositoryRoot(), "src", "KeyLoad.AppHost"),
            Args = args
        });

    private static bool IsRf3Node(ContainerResource resource)
        => resource.Name is "node1" or "node2" or "node3";

    private static string RepositoryRoot()
    {
        for (var directory = AppContext.BaseDirectory; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (File.Exists(Path.Combine(directory, "KeyLoad.slnx")))
            {
                return directory;
            }
        }
        throw new InvalidOperationException("The Aspire model test requires the source checkout.");
    }
}
