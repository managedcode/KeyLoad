using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
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

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        var selection = ComparisonWorkerSelection.Read(new ConfigurationBuilder().AddEnvironmentVariables().Build());
        var evidence = IsolatedNativeReportAssertions.EvidenceDirectory();
        var root = Path.Combine(Path.GetTempPath(), TemporaryPrefix + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, Reports);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(60));
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(
            [RootArgument + root, OutputArgument + output, EnableArgument], timeout.Token);
        builder.Services.AddLogging(logging => logging.ClearProviders().AddConsole().SetMinimumLevel(LogLevel.Warning));
        var app = await builder.BuildAsync(timeout.Token);
        var containers = app.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<ContainerResource>().ToArray();
        await using var capture = new ComparisonTestLogCapture(app, containers.Select(container => container.Name),
            ComparisonProgressLine.PathForEvidenceDirectory(evidence));
        var primaryFailure = false;
        try
        {
            await IsolatedNativeReportAssertions.VerifyModelAsync(containers, selection, timeout.Token);
            var exitCode = await AspireResourceCompletion.RunToExitAsync(app, Runner, timeout.Token);
            await Assert.That(exitCode).IsEqualTo(0);
            await IsolatedNativeReportAssertions.VerifyReportAsync(output, selection, timeout.Token);
            await IsolatedNativeRegressions.VerifyAsync(app, selection, timeout.Token);
        }
        catch (Exception)
        {
            primaryFailure = true;
            throw;
        }
        finally
        {
            await IsolatedNativeTeardown.CompleteAsync(app, capture, output, evidence, root, containers, primaryFailure);
        }
    }
}
