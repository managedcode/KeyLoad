using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TUnit.Assertions.Enums;

namespace KeyLoad.ComparisonTests.Features.StorageRecovery;

internal sealed class PriorProbePrerequisiteTests
{
    private const string RecoveryArgument = "--KeyLoadTests:Suite=recovery";
    private const string ResultsArgument = "--KeyLoadTests:ResultsDirectory=TestResults/prior probe model";
    private const string RunnerName = "tests-recovery";
    private const string ProbeEnvironment = "KeyLoadTests__PriorProbesDirectory";
    private const string Script = "scripts/Features/StorageRecovery/build-prior-probe.sh";
    private const string DestinationArgument = "--destination=";
    private const string EpochArgument = "--epoch=";
    private const string Shell = "bash";
    private const string SourceMissing = "The prior-probe model requires the KeyLoad source checkout.";
    private const string DirectoryMissing = "The recovery runner has no owned prior-probe directory.";
    private static readonly (string Resource, string Directory, string Epoch)[] Profiles =
    [
        ("prepare-native5-probe", "native5-probe", "5"),
        ("prepare-native6-probe", "native6-probe", "6")
    ];

    [Test]
    public async Task AcEpoch7006RecoveryWaitsForBothExactProfilesBeforeRunnerAdmission()
    {
        await using var app = CreateApplication();
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        await Assert.That(model.Resources.OfType<ContainerResource>()).IsEmpty();
        var executables = model.Resources.OfType<ExecutableResource>().ToArray();
        await Assert.That(executables.Length).IsEqualTo(3);
        var runner = executables.Single(resource => resource.Name == RunnerName);
        var directory = await ReadProbeDirectoryAsync(runner);
        var waits = runner.Annotations.OfType<WaitAnnotation>().ToArray();
        await Assert.That(waits.Length).IsEqualTo(Profiles.Length);
        foreach (var profile in Profiles)
        {
            var resource = executables.Single(candidate => candidate.Name == profile.Resource);
            var wait = waits.Single(candidate => ReferenceEquals(candidate.Resource, resource));
            await Assert.That(wait.WaitType).IsEqualTo(WaitType.WaitForCompletion);
            await Assert.That(wait.ExitCode).IsEqualTo(0);
            await Assert.That(resource.Command).IsEqualTo(Shell);
            await Assert.That(resource.WorkingDirectory).IsEqualTo(RepositoryRoot());
            var configuration = await ReadConfigurationAsync(resource);
            await Assert.That(configuration.Arguments.Select(argument => argument.Value)).IsEquivalentTo(
                new[] { Script, EpochArgument + profile.Epoch, DestinationArgument + Path.Combine(directory, profile.Directory) },
                CollectionOrdering.Matching);
            await Assert.That(resource.Annotations.OfType<WaitAnnotation>()).IsEmpty();
        }
    }

    [Test]
    public async Task AcEpoch7006RepeatedAppHostsCannotOverwriteAnotherRunsProbeArtifacts()
    {
        await using var first = CreateApplication();
        await using var second = CreateApplication();
        var firstDirectory = await ReadProbeDirectoryAsync(Runner(first));
        var secondDirectory = await ReadProbeDirectoryAsync(Runner(second));
        await Assert.That(firstDirectory).IsNotEqualTo(secondDirectory);
        await Assert.That(Path.GetDirectoryName(firstDirectory)).IsEqualTo(Path.GetDirectoryName(secondDirectory));
        var parent = Path.Combine(RepositoryRoot(), "TestResults", "prior probe model", "prior-probes");
        await Assert.That(Path.GetDirectoryName(firstDirectory)).IsEqualTo(parent);
        foreach (var directory in new[] { firstDirectory, secondDirectory })
        {
            await Assert.That(Path.IsPathFullyQualified(directory)).IsTrue();
            await Assert.That(Guid.TryParseExact(Path.GetFileName(directory), "N", out var identity)).IsTrue();
            await Assert.That(identity).IsNotEqualTo(Guid.Empty);
        }
    }

    private static ExecutableResource Runner(DistributedApplication app)
        => app.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ExecutableResource>().Single(resource => resource.Name == RunnerName);

    private static async Task<string> ReadProbeDirectoryAsync(ExecutableResource runner)
    {
        var configuration = await ReadConfigurationAsync(runner);
        return configuration.EnvironmentVariables.Single(variable => variable.Key == ProbeEnvironment).Value
            ?? throw new InvalidOperationException(DirectoryMissing);
    }

    private static async Task<IExecutionConfigurationResult> ReadConfigurationAsync(ExecutableResource resource)
    {
        var configuration = await ExecutionConfigurationBuilder.Create(resource).WithEnvironmentVariablesConfig()
            .WithArgumentsConfig().BuildAsync(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
                NullLogger.Instance, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(configuration.Exception).IsNull();
        return configuration;
    }

    private static DistributedApplication CreateApplication()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            DisableDashboard = true,
            ProjectDirectory = Path.Combine(RepositoryRoot(), "src", "KeyLoad.AppHost"),
            Args = [RecoveryArgument, ResultsArgument]
        });
        KeyLoadAppHostApplication.AddKeyLoad(builder);
        return builder.Build();
    }

    private static string RepositoryRoot()
    {
        for (var directory = AppContext.BaseDirectory; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (File.Exists(Path.Combine(directory, "KeyLoad.slnx")))
            {
                return directory;
            }
        }
        throw new InvalidOperationException(SourceMissing);
    }
}
