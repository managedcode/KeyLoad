using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions.Enums;

namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal sealed class AspireTestEntryArtifactModelTests
{
    private const string BenchmarksTargetEnvironment = "Benchmarks__Target";
    private const string SuiteEnvironment = "KeyLoadTests__Suite";

    [Test]
    public async Task ExplicitArtifactPathsAndCollectorsRemainSeparateNativeArguments()
    {
        var builder = CreateBuilder(
            "--KeyLoadTests:Suite=unit",
            "--KeyLoadTests:ResultsDirectory=artifacts/test results",
            "--KeyLoadTests:ReportTrx=true",
            "--KeyLoadTests:CoverageSettings=config/coverage settings.runsettings",
            "--KeyLoadTests:CoverageOutput=artifacts/coverage results");
        KeyLoadAppHostApplication.AddKeyLoad(builder);
        var app = builder.Build();
        try
        {
            var runner = app.Services.GetRequiredService<DistributedApplicationModel>()
                .Resources.OfType<ExecutableResource>().Single();
            var configuration = await ReadConfigurationAsync(runner);
            var root = RepositoryRoot();
            await Assert.That(configuration.Arguments.Select(argument => argument.Value)).IsEquivalentTo(
            new[]
            {
                "test", "--project", Path.Combine(root, "tests", "KeyLoad.UnitTests"), "--no-build", "--no-restore",
                "--configuration", "Release", "--results-directory", Path.Combine(root, "artifacts/test results"),
                "--report-trx", "--coverage", "--coverage-settings", Path.Combine(root, "config/coverage settings.runsettings"),
                "--coverage-output-format", "cobertura", "--coverage-output", Path.Combine(root, "artifacts/coverage results")
            }, CollectionOrdering.Matching);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Test]
    public async Task ComparisonTargetPassesOnlyToItsSingleChildRunner()
    {
        var builder = CreateBuilder("--KeyLoadTests:Suite=comparison", "--Benchmarks:Target=ZoneTree");
        KeyLoadAppHostApplication.AddKeyLoad(builder);
        var app = builder.Build();
        try
        {
            var model = app.Services.GetRequiredService<DistributedApplicationModel>();
            await Assert.That(model.Resources.Count).IsEqualTo(1);
            await Assert.That(model.Resources.OfType<ContainerResource>()).IsEmpty();
            var runner = model.Resources.OfType<ExecutableResource>().Single();
            var configuration = await ReadConfigurationAsync(runner);
            var environment = configuration.EnvironmentVariables.ToDictionary();
            await Assert.That(environment[BenchmarksTargetEnvironment]).IsEqualTo("ZoneTree");
            await Assert.That(environment[SuiteEnvironment]).IsEqualTo("");
        }
        finally
        {
            await app.DisposeAsync();
        }

        await AssertRejectedBeforeResourcesAsync("--KeyLoadTests:Suite=comparison", "--Benchmarks:Enabled=true");
        await AssertRejectedBeforeResourcesAsync("--KeyLoadTests:Suite=unit", "--Benchmarks:Target=ZoneTree");
    }

    [Test]
    public async Task InvalidArtifactPairsAndUnboundedOrNullValuesRejectBeforeResources()
    {
        await AssertRejectedBeforeResourcesAsync("--KeyLoadTests:Suite=unit", "--KeyLoadTests:CoverageSettings=coverage.runsettings");
        await AssertRejectedBeforeResourcesAsync("--KeyLoadTests:Suite=unit", "--KeyLoadTests:CoverageOutput=coverage.xml");
        await AssertRejectedBeforeResourcesAsync("--KeyLoadTests:Suite=unit", "--KeyLoadTests:CoverageSettings= ",
            "--KeyLoadTests:CoverageOutput=coverage.xml");
        await AssertRejectedBeforeResourcesAsync("--KeyLoadTests:Suite=unit", "--KeyLoadTests:CoverageSettings=settings.runsettings",
            "--KeyLoadTests:CoverageOutput= ");
        await AssertRejectedBeforeResourcesAsync("--KeyLoadTests:Suite=unit",
            "--KeyLoadTests:ResultsDirectory=" + new string('x', 4_097));
        await AssertRejectedBeforeResourcesAsync("--KeyLoadTests:Suite=unit",
            "--KeyLoadTests:CoverageSettings=" + new string('x', 4_097), "--KeyLoadTests:CoverageOutput=result.xml");
        await AssertRejectedBeforeResourcesAsync("--KeyLoadTests:Suite=unit", "--KeyLoadTests:CoverageSettings=settings\0.runsettings",
            "--KeyLoadTests:CoverageOutput=result.xml");
        await AssertRejectedBeforeResourcesAsync("--KeyLoadTests:Suite=unit", "--KeyLoadTests:CoverageSettings=settings.runsettings",
            "--KeyLoadTests:CoverageOutput=result\0.xml");
        await AssertRejectedBeforeResourcesAsync("--KeyLoadTests:Suite=unit", "--KeyLoadTests:ResultsDirectory=bad\0path");
        await AssertRejectedBeforeResourcesAsync("--KeyLoadTests:Suite=unit", "--KeyLoadTests:Filter=bad\0filter");
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
