using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class OpenLoopAspirePlanTestBuilder
{
    internal static IDistributedApplicationBuilder CreateBuilder(
        bool cancellationProof = false, params KeyValuePair<string, string?>[] overrides)
    {
        var root = RepositoryRoot();
        var builder = CreateAspireBuilder(root);
        var values = CreateDefaultConfiguration(cancellationProof);
        ApplyOverrides(values, overrides);
        builder.Configuration.AddInMemoryCollection(values);
        return builder;
    }

    private static IDistributedApplicationBuilder CreateAspireBuilder(string root)
        => DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = [],
            DisableDashboard = true,
            AssemblyName = typeof(KeyLoadAppHostApplication).Assembly.GetName().Name,
            ProjectDirectory = Path.Combine(root, "src", "KeyLoad.AppHost")
        });

    private static Dictionary<string, string?> CreateDefaultConfiguration(bool cancellationProof)
    {
        var profile = "scaled-100k-c16";
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [TestSuiteProtocol.SuiteSetting] = "comparison",
            [TestSuiteProtocol.FilterSetting] = cancellationProof
                ? "/*/*/IsolatedNativeOpenLoopCancellationTests/*"
                : "/*/*/IsolatedNativeOpenLoopComparisonTests/*",
            [TestSuiteProtocol.ScaleProfileSetting] = profile,
            [TestSuiteProtocol.OpenLoopRateSetting] = "250",
            [TestSuiteProtocol.TimeoutSetting] = "140",
            [TestSuiteProtocol.VectorProfileSetting] = null,
            [TestOrchestrationConfigurationKeys.KeyLoadTestsLocalRf3ImageEnabled] = null,
            [TestOrchestrationConfigurationKeys.BenchmarksTarget] = "KeyLoad",
            [TestOrchestrationConfigurationKeys.BenchmarksNodeCount] = "3",
            [TestOrchestrationConfigurationKeys.BenchmarksScenario] = "PointRead",
            [TestOrchestrationConfigurationKeys.BenchmarksEvidenceProfile] = profile,
            [TestSuiteProtocol.AppHostBenchmarkProfileSetting] = "general",
            [TestSuiteProtocol.BenchmarkEnabledSetting] = null,
            [TestOrchestrationConfigurationKeys.BenchmarksOpenLoopRate] = null,
            [TestSuiteProtocol.OpenLoopCancellationProofSetting] = null,
            [TestOrchestrationConfigurationKeys.BenchmarksScaleProfile] = null,
            [TestOrchestrationConfigurationKeys.BenchmarksVectorProfile] = null,
            [TestOrchestrationConfigurationKeys.BenchmarksDocuments] = null,
            [TestOrchestrationConfigurationKeys.BenchmarksOperations] = null,
            [TestOrchestrationConfigurationKeys.BenchmarksWarmup] = null,
            [TestOrchestrationConfigurationKeys.BenchmarksRepetitions] = null,
            [TestOrchestrationConfigurationKeys.BenchmarksConcurrency] = null,
            [TestOrchestrationConfigurationKeys.BenchmarksPayloadBytes] = null,
            [TestOrchestrationConfigurationKeys.BenchmarksSeed] = null,
            [TestOrchestrationConfigurationKeys.BenchmarksDimensions] = null,
            [TestOrchestrationConfigurationKeys.BenchmarksTopK] = null,
            [TestOrchestrationConfigurationKeys.BenchmarksTimeoutSeconds] = null,
            [TestOrchestrationConfigurationKeys.BenchmarksGraphVertices] = null,
            [TestOrchestrationConfigurationKeys.BenchmarksGraphFanOut] = null,
            [TestOrchestrationConfigurationKeys.BenchmarksGraphDepth] = null
        };
        return values;
    }

    private static void ApplyOverrides(Dictionary<string, string?> values,
        KeyValuePair<string, string?>[] overrides)
    {
        foreach (var pair in overrides)
        {
            values[pair.Key] = pair.Value;
        }
    }

    internal static IDistributedApplicationBuilder CreateBuilderWithCommandLineArguments(
        string[] commandLineArguments, params KeyValuePair<string, string?>[] overrides)
    {
        var builder = CreateBuilder(overrides: overrides);
        builder.Configuration.AddCommandLine(commandLineArguments);
        return builder;
    }

    internal static async Task<OpenLoopAspirePlanSnapshot> ComposeAndReadAsync(
        IDistributedApplicationBuilder builder)
    {
        KeyLoadAppHostApplication.AddKeyLoad(builder);
        await using var application = builder.Build();
        var model = application.Services.GetRequiredService<DistributedApplicationModel>();
        var resource = model.Resources.OfType<ExecutableResource>()
            .Single(item => item.Name == "tests-comparison");
        var resolved = await ExecutionConfigurationBuilder.Create(resource)
            .WithArgumentsConfig()
            .WithEnvironmentVariablesConfig()
            .BuildAsync(application.Services.GetRequiredService<DistributedApplicationExecutionContext>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, TestContext.Current!.Execution.CancellationToken);
        if (resolved.Exception is { } failure)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        var values = resolved.EnvironmentVariables.ToDictionary(static item => item.Key,
            static item => (string?)item.Value, StringComparer.Ordinal);
        return new(resolved.Arguments.Select(static value => value.Value).ToArray(), values);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KeyLoad.slnx")))
            {
                return directory.FullName;
            }
        }
        throw new InvalidOperationException("The KeyLoad repository root was not found.");
    }
}

internal sealed record OpenLoopAspirePlanSnapshot(
    string[] Arguments,
    IReadOnlyDictionary<string, string?> Environment);
