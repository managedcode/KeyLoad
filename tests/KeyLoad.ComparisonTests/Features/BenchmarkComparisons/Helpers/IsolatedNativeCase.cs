using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.ExceptionServices;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

    internal static async Task RunAsync(IsolatedNativeCaseIntent intent, CancellationToken cancellationToken)
    {
        var plan = IsolatedNativeCaseSelection.Read(
            new ConfigurationBuilder().AddEnvironmentVariables().Build(), intent);
        var execution = NativeExecutionPolicyFixture.Harness();
        var provenance = BenchmarkProvenanceRegistration.Bind();
        var evidence = IsolatedNativeReportAssertions.EvidenceDirectory();
        var root = Path.Combine(Path.GetTempPath(), TemporaryPrefix + Guid.NewGuid().ToString(OpenLoopNativeTestOracle.GuidFormat));
        var output = Path.Combine(root, Reports);
        using var timeoutTimeout = new CancellationTokenSource(plan.Timeout, TimeProvider.System);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutTimeout.Token);
        var args = CreateArguments(plan, root, output);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(args, timeout.Token);
        builder.Services.AddLogging(logging => logging.ClearProviders().AddConsole().SetMinimumLevel(LogLevel.Warning));
        var app = await builder.BuildAsync(timeout.Token);
        await RunWithTeardownAsync(app, plan, output, evidence, root, provenance,
            execution, timeout.Token);
    }

    private static async Task RunWithTeardownAsync(DistributedApplication app, IsolatedNativeCasePlan plan,
        string output, string evidence, string root, IOptions<BenchmarkProvenanceOptions> provenance,
        IOptions<NativeComparisonHarnessOptions> execution, CancellationToken token)
    {
        ContainerResource[] containers = [];
        var work = new IsolatedNativeOwnedWork(execution);
        var failures = new List<Exception>();
        var teardownStarted = false;
        async Task RunOwnedAsync()
        {
            containers = app.Services.GetRequiredService<DistributedApplicationModel>()
                .Resources.OfType<ContainerResource>().ToArray();
            work.CancellationControl = plan.CancellationProof
                ? new OpenLoopNativeCancellationControl(plan, output, execution, token) : null;
            Action<string>? observer = work.CancellationControl is { } control ? control.Observe : null;
            await using var capture = new ComparisonTestLogCapture(app, execution,
                containers.Select(container => container.Name), ComparisonProgressLine.PathForEvidenceDirectory(evidence), observer);
            var original = RunOwnedCaseAsync(app, containers, plan, output, root, work, provenance, token);
            var bodyFailures = new List<Exception>();
            await IsolatedNativeTeardownNativeSupport.CollectFailureAsync(() => original, bodyFailures);
            var primary = OpenLoopFailure.Combine(null, bodyFailures.ToImmutableArray());
            teardownStarted = true;
            await IsolatedNativeTeardownNativeSupport.CollectFailureAsync(() =>
                IsolatedNativeTeardown.CompleteAsync(app, capture, output, evidence, root, containers, work, primary), failures);
        }
        await IsolatedNativeTeardownNativeSupport.CollectFailureAsync(RunOwnedAsync, failures);
        if (!teardownStarted)
        {
            var primary = OpenLoopFailure.Combine(null, failures.ToImmutableArray());
            await IsolatedNativeTeardownNativeSupport.CollectFailureAsync(() =>
                IsolatedNativeTeardown.CompleteAsync(app, null, output, evidence, root, containers, work, primary), failures);
        }
        if (OpenLoopFailure.Combine(null, failures.ToImmutableArray()) is { } failure)
        { ExceptionDispatchInfo.Capture(failure).Throw(); }
    }

    private static string[] CreateArguments(IsolatedNativeCasePlan plan, string root, string output)
    {
        var selection = plan.Selection;
        var arguments = new List<string> { RootArgument + root, OutputArgument + output, EnableArgument };
        if (selection.ScaledProfile is { } scaleProfile)
        {
            arguments.Add(ScaleArgument + scaleProfile.Id);
        }
        if (selection.VectorProfile is { } vectorProfile)
        {
            arguments.Add(VectorArgument + vectorProfile.Id);
        }
        if (selection.OpenLoopRate is { } rate)
        {
            arguments.Add(IsolatedNativeCaseSelection.RateArgument + rate.ToString(CultureInfo.InvariantCulture));
        }
        if (plan.CancellationProof)
        {
            arguments.Add(IsolatedNativeCaseSelection.ProofArgument);
        }

        return arguments.ToArray();
    }

    private static async Task RunOwnedCaseAsync(DistributedApplication app, ContainerResource[] containers,
        IsolatedNativeCasePlan plan, string output, string root, IsolatedNativeOwnedWork work,
        IOptions<BenchmarkProvenanceOptions> provenance, CancellationToken token)
    {
        var selection = plan.Selection;
        await IsolatedNativeReportAssertions.VerifyModelAsync(containers, selection, token);
        if (selection.ScaledProfile is not null || selection.VectorProfile is not null)
        {
            work.Collector = app.Services.GetService<ScaleServerResourceEvidenceCollector>();
            work.Observation = work.Collector?.StartAsync(containers,
                readinessToken => ScaleServerResourceReadiness.WaitAsync(app, containers, readinessToken), token)
                ?? throw new InvalidOperationException(OpenLoopNativeTestOracle.CollectorUnavailable);
        }
        var exitCode = await work.RunAsync(app, Runner, token);
        await Assert.That(exitCode).IsEqualTo(OpenLoopNativeTestOracle.SuccessfulRunnerExitCode);
        if (work.Collector is not null)
        {
            await work.StartSettlement();
            await VerifyResourceEvidenceAsync(app, output, plan, provenance, token);
        }
        await VerifyResultAsync(output, plan, work.CancellationControl, provenance, token);
        await ScaleServerCancellationProbe.VerifyAsync(app, containers, selection, root, token);
        await IsolatedNativeRegressions.VerifyAsync(app, selection, token);
    }

    private static async Task VerifyResultAsync(string output, IsolatedNativeCasePlan plan,
        OpenLoopNativeCancellationControl? cancellationControl,
        IOptions<BenchmarkProvenanceOptions> provenance, CancellationToken token)
    {
        var selection = plan.Selection;
        var unavailable = IsolatedComparisonContract.Current.UnsupportedTopologies.Any(item =>
            item.Target == selection.Target && item.NodeCounts.Contains(selection.NodeCount));
        if (!plan.OpenLoop || unavailable)
        {
            await IsolatedNativeReportAssertions.VerifyReportAsync(output, selection, token);
        }
        else if (plan.CancellationProof)
        {
            var marker = await (cancellationControl
                ?? throw new InvalidOperationException(OpenLoopNativeTestOracle.MarkerUnavailable)).ObservedMarker;
            await IsolatedNativeOpenLoopCancellationAssertions.VerifyAsync(output, selection,
                selection.OpenLoopRate!.Value, marker, provenance, token);
        }
        else
        {
            await IsolatedNativeOpenLoopAssertions.VerifyAsync(output, selection, selection.OpenLoopRate!.Value,
                provenance, token);
        }
    }

    private static async Task VerifyResourceEvidenceAsync(DistributedApplication app, string output,
        IsolatedNativeCasePlan plan, IOptions<BenchmarkProvenanceOptions> provenance, CancellationToken token)
    {
        var options = app.Services.GetRequiredService<IOptions<ScaleServerResourceOptions>>();
        var unavailable = IsolatedComparisonContract.Current.UnsupportedTopologies.Any(item =>
            item.Target == plan.Selection.Target && item.NodeCounts.Contains(plan.Selection.NodeCount));
        if (!plan.OpenLoop || unavailable)
        {
            await IsolatedNativeReportAssertions.VerifyServerResourceEvidenceAsync(output, plan.Selection, options, token);
        }
        else
        {
            await IsolatedNativeOpenLoopResourceAssertions.VerifyAsync(output, plan, options, provenance, token);
        }
    }
}
