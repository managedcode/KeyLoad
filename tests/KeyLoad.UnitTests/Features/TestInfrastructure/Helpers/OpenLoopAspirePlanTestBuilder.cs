using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
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
            AppHostFilePath = Path.Combine(root, "src", "KeyLoad.AppHost", "KeyLoad.AppHost.csproj"),
            ProjectDirectory = Path.Combine(root, "src", "KeyLoad.AppHost")
        });

    private static Dictionary<string, string?> CreateDefaultConfiguration(bool cancellationProof)
    {
        var profile = "scaled-100k-c16";
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["KeyLoadTests:Suite"] = "comparison",
            ["KeyLoadTests:Filter"] = cancellationProof
                ? "/*/*/IsolatedNativeOpenLoopCancellationTests/*"
                : "/*/*/IsolatedNativeOpenLoopComparisonTests/*",
            ["KeyLoadTests:ScaleProfile"] = profile,
            ["KeyLoadTests:OpenLoopRate"] = "250",
            ["KeyLoadTests:TimeoutMinutes"] = "140",
            ["KeyLoadTests:VectorProfile"] = null,
            ["KeyLoadTests:LocalRf3Image:Enabled"] = null,
            ["Benchmarks:Target"] = "KeyLoad",
            ["Benchmarks:NodeCount"] = "3",
            ["Benchmarks:Scenario"] = "PointRead",
            ["Benchmarks:EvidenceProfile"] = profile,
            ["Benchmarks:Profile"] = "general",
            ["Benchmarks:Enabled"] = null,
            ["Benchmarks:OpenLoopRate"] = null,
            ["Benchmarks:OpenLoopCancellationProof"] = null,
            ["Benchmarks:ScaleProfile"] = null,
            ["Benchmarks:VectorProfile"] = null,
            ["Benchmarks:Documents"] = null,
            ["Benchmarks:Operations"] = null,
            ["Benchmarks:Warmup"] = null,
            ["Benchmarks:Repetitions"] = null,
            ["Benchmarks:Concurrency"] = null,
            ["Benchmarks:PayloadBytes"] = null,
            ["Benchmarks:Seed"] = null,
            ["Benchmarks:Dimensions"] = null,
            ["Benchmarks:TopK"] = null,
            ["Benchmarks:TimeoutSeconds"] = null,
            ["Benchmarks:GraphVertices"] = null,
            ["Benchmarks:GraphFanOut"] = null,
            ["Benchmarks:GraphDepth"] = null
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
        var arguments = await resource.GetArgumentValuesAsync(DistributedApplicationOperation.Run);
        var environment = await resource.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Run);
        var values = environment.ToDictionary(static item => item.Key,
            static item => item.Value?.ToString(), StringComparer.Ordinal);
        return new(arguments.Select(static value => value?.ToString() ?? string.Empty).ToArray(), values);
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
