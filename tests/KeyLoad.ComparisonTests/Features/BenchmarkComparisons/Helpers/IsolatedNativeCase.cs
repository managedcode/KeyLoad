using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedNativeCase
{
    private const string TemporaryPrefix = "keyload-isolated-case-";
    private const string Reports = "reports";
    private const string Runner = "comparisons";
    private const string RootArgument = "--Benchmarks:DataRoot=";
    private const string OutputArgument = "--Benchmarks:Output=";
    private const string EnableArgument = "--Benchmarks:Enabled=true";
    private const string ScaleArgument = "--Benchmarks:ScaleProfile=";
    private const string VectorArgument = "--Benchmarks:VectorProfile=";

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        var selection = ComparisonWorkerSelection.Read(new ConfigurationBuilder().AddEnvironmentVariables().Build());
        var evidence = IsolatedNativeReportAssertions.EvidenceDirectory();
        var root = Path.Combine(Path.GetTempPath(), TemporaryPrefix + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, Reports);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(selection.VectorProfile is not null ? 145 : selection.ScaledProfile is null ? 60 : 140));
        var args = CreateArguments(selection, root, output);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(args, timeout.Token);
        builder.Services.AddLogging(logging => logging.ClearProviders().AddConsole().SetMinimumLevel(LogLevel.Warning));
        var app = await builder.BuildAsync(timeout.Token);
        ContainerResource[] containers = [];
        ComparisonTestLogCapture? capture = null;
        var work = new IsolatedNativeOwnedWork();
        Exception? primaryFailure = null;
        try
        {
            containers = app.Services.GetRequiredService<DistributedApplicationModel>()
                .Resources.OfType<ContainerResource>().ToArray();
            capture = new ComparisonTestLogCapture(app, containers.Select(container => container.Name),
                ComparisonProgressLine.PathForEvidenceDirectory(evidence));
            await RunOwnedCaseAsync(app, containers, selection, work, timeout.Token);
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
        }
        await IsolatedNativeTeardown.CompleteAsync(app, capture, output, evidence, root, containers, work, primaryFailure);
    }

    private static string[] CreateArguments(ComparisonWorkerSelection selection, string root, string output)
    {
        var arguments = new List<string> { RootArgument + root, OutputArgument + output, EnableArgument };
        if (selection.ScaledProfile is { } scaleProfile)
        {
            arguments.Add(ScaleArgument + scaleProfile.Id);
        }
        if (selection.VectorProfile is { } vectorProfile)
        { arguments.Add(VectorArgument + vectorProfile.Id); }

        return arguments.ToArray();
    }

    private static async Task RunOwnedCaseAsync(DistributedApplication app, ContainerResource[] containers,
        ComparisonWorkerSelection selection, IsolatedNativeOwnedWork work,
        CancellationToken token)
    {
        await IsolatedNativeReportAssertions.VerifyModelAsync(containers, selection, token);
        if (selection.ScaledProfile is not null || selection.VectorProfile is not null)
        {
            work.Collector = app.Services.GetService<ScaleServerResourceEvidenceCollector>();
            work.Observation = work.Collector?.StartAsync(containers,
                readinessToken => ScaleServerResourceReadiness.WaitAsync(app, containers, readinessToken), token)
                ?? throw new InvalidOperationException("Scale server evidence collector is not registered.");
        }
        var exitCode = await AspireResourceCompletion.RunToExitAsync(app, Runner, token);
        await Assert.That(exitCode).IsEqualTo(0);
        if (work.Collector is not null)
        {
            await work.StartSettlement();
            await IsolatedNativeReportAssertions.VerifyServerResourceEvidenceAsync(output, selection, token);
        }
        await IsolatedNativeReportAssertions.VerifyReportAsync(output, selection, token);
        await ScaleServerCancellationProbe.VerifyAsync(app, containers, selection, root, token);
        await IsolatedNativeRegressions.VerifyAsync(app, selection, token);
    }
}
