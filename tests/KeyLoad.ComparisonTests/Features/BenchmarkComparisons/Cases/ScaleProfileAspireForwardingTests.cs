using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Comparisons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class ScaleProfileAspireForwardingTests
{
    private const string Profile = "scaled-100k-c16";
    private const string SuiteEnvironment = "KeyLoadTests__Suite";
    private const string ScaleHarnessEnvironment = "KeyLoadTests__ScaleProfile";
    private const string WorkerProfileEnvironment = "Benchmarks__ScaleProfile";
    private const string TargetEnvironment = "Benchmarks__Target";
    private const string SuiteResource = "tests-comparison";
    private const string TestRootPrefix = "keyload-scale-forwarding-";
    private const string Filter = "/*/*/IsolatedNativeComparisonTests/*";

    [Test]
    public async Task AcScale014AspireRunnerReceivesWorkerProfileAndClearsHarnessSelector()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var root = Directory.CreateTempSubdirectory(TestRootPrefix).FullName;
        var arguments = new[]
        {
            "--KeyLoadTests:Suite=comparison", "--KeyLoadTests:Filter=" + Filter,
            "--KeyLoadTests:ScaleProfile=" + Profile, "--KeyLoadTests:TimeoutMinutes=140",
            "--Benchmarks:Enabled=false", "--Benchmarks:Target=KeyLoad", "--Benchmarks:NodeCount=3",
            "--Benchmarks:Scenario=DocumentWrite", "--Benchmarks:EvidenceProfile=" + Profile,
            "--KeyLoadTests:ResultsDirectory=" + root
        };
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(arguments, token);
        try
        {
            await using var app = await builder.BuildAsync(token);
            var runner = app.Services.GetRequiredService<DistributedApplicationModel>().Resources
                .OfType<ExecutableResource>().Single(resource => resource.Name == SuiteResource);
            var config = await ExecutionConfigurationBuilder.Create(runner).WithEnvironmentVariablesConfig()
                .WithArgumentsConfig().BuildAsync(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Publish),
                    NullLogger.Instance, token);
            await Assert.That(config.Exception).IsNull();
            var environment = config.EnvironmentVariables.ToDictionary();
            await Assert.That(environment[SuiteEnvironment]).IsEqualTo(string.Empty);
            await Assert.That(environment[ScaleHarnessEnvironment]).IsEqualTo(string.Empty);
            await Assert.That(environment[WorkerProfileEnvironment]).IsEqualTo(Profile);
            await Assert.That(environment[TargetEnvironment]).IsEqualTo("KeyLoad");
            var values = config.Arguments.Select(argument => argument.Value).ToArray();
            await Assert.That(values).DoesNotContain("--KeyLoadTests:ScaleProfile=" + Profile);
            await Assert.That(values).Contains("--treenode-filter");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
