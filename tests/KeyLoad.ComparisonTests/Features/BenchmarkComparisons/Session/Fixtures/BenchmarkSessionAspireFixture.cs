using System.Runtime.ExceptionServices;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Owns the single Aspire deployment, native workload runner and original artifact lifetime.</summary>
internal static class BenchmarkSessionAspireFixture
{
    private const string Enabled = "--Benchmarks:Enabled=true";
    private const string Session = "--Benchmarks:Session=true";
    private const string Profile = "--Benchmarks:EvidenceProfile=session-v1";
    private const string RootArgument = "--Benchmarks:DataRoot=";
    private const string OutputArgument = "--Benchmarks:Output=";
    private const string IdentityArgument = "--Benchmarks:SessionId=";
    private const string Runner = "comparisons";
    private const string RuntimeDirectory = "runtime";
    private const string Solution = "KeyLoad.slnx";

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        using var configuration = new ConfigurationManager();
        configuration.AddEnvironmentVariables();
        var selected = BenchmarkSessionCallerOptions.Read(configuration).Value;
        var selection = BenchmarkSessionContract.ResourceSelection(selected.Target, selected.NodeCount);
        var root = Path.Combine(RepositoryRoot(), "artifacts", "comparisons", "sessions", RuntimeDirectory,
            selected.SessionId + Guid.NewGuid().ToString("N"));
        var evidence = Path.GetFullPath(selected.EvidenceDirectory);
        Directory.CreateDirectory(evidence);
        var execution = AppHostOptionsRegistration.BindTestExecution(configuration);
        using var deadline = new CancellationTokenSource(execution.Value.NativeScaledTimeout, TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var arguments = new[] { Enabled, Session, Profile, RootArgument + root, OutputArgument + evidence,
            IdentityArgument + selected.SessionId };
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(arguments, lifetime.Token);
        builder.Services.AddLogging(logging => logging.ClearProviders().AddConsole().SetMinimumLevel(LogLevel.Warning));
        var application = await builder.BuildAsync(lifetime.Token);
        await RunOwnedAsync(application, selection, selected, evidence, root, lifetime.Token);
    }

    private static async Task RunOwnedAsync(DistributedApplication application, BenchmarkSessionSelection selection,
        BenchmarkSessionCallerOptions selected, string evidence, string root, CancellationToken token)
    {
        var containers = application.Services.GetRequiredService<DistributedApplicationModel>()
            .Resources.OfType<ContainerResource>().ToArray();
        var execution = NativeExecutionPolicyFixture.Harness();
        var work = new IsolatedNativeOwnedWork(execution);
        Exception? primary = null;
        try
        {
            await IsolatedNativeReportAssertions.VerifyModelAsync(containers, selection, token);
            work.BeginCapture(application, containers.Select(container => container.Name),
                ComparisonProgressLine.PathForEvidenceDirectory(evidence));
            work.Collector = application.Services.GetService<ScaleServerResourceEvidenceCollector>();
            if (work.Collector is not null && !BenchmarkSessionContract.UnsupportedTopologies.Any(item =>
                item.Target == selected.Target && item.NodeCounts.Contains(selected.NodeCount)))
            {
                work.Observation = work.Collector.StartAsync(containers,
                    readinessToken => ScaleServerResourceReadiness.WaitAsync(application, containers, readinessToken), token);
            }
            else
            {
                work.Collector = null;
            }
            var exit = await work.RunAsync(application, Runner, token);
            if (work.Collector is not null)
            {
                await work.StartSettlement();
            }
            var qualificationFailure = await BenchmarkSessionQualifications.RunAsync(application, containers, selection,
                selected, evidence, root, exit, token);
            using var retention = new CancellationTokenSource(execution.Value.TeardownReceiptTimeout, TimeProvider.System);
            await BenchmarkSessionManifest.WriteAsync(evidence, selected, exit, qualificationFailure is not null, retention.Token);
            if (qualificationFailure is not null)
            {
                ExceptionDispatchInfo.Capture(qualificationFailure).Throw();
            }
            await Assert.That(exit).IsEqualTo(0);
        }
        catch (Exception error)
        {
            primary = error;
            throw;
        }
        finally
        {
            var teardown = IsolatedNativeTeardown.CompleteAsync(application, evidence, evidence, root,
                containers, work, primary);
            try
            {
                await teardown;
            }
            finally
            {
                await IsolatedNativeTeardown.SettleOwnerDisposalAsync(teardown, work.DisposeAsync().AsTask());
            }
        }
    }

    private static string RepositoryRoot()
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
