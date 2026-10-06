using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Owns actual Aspire resource notification and logger services without starting database stand-ins.</summary>
internal sealed class ComparisonProgressNativeFixture : IAsyncDisposable
{
    internal const string RunnerName = "comparisons";
    internal const string NodeName = "progress-native-node";
    private const string Image = "mcr.microsoft.com/dotnet/runtime:10.0";
    private static TimeSpan Deadline => NativeExecutionPolicyFixture.Harness().Value.FixtureDisposalTimeout;

    private ComparisonProgressNativeFixture(DistributedApplication application,
        ContainerResource runner, ContainerResource node, string progressPath)
    {
        Application = application;
        Runner = runner;
        Node = node;
        Capture = new ComparisonTestLogCapture(application, NativeExecutionPolicyFixture.Harness(), [NodeName], progressPath);
    }

    internal DistributedApplication Application { get; }
    internal ContainerResource Runner { get; }
    internal ContainerResource Node { get; }
    internal ComparisonTestLogCapture Capture { get; }

    internal static async Task<ComparisonProgressNativeFixture> CreateAsync(string progressPath)
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true });
        var runner = builder.AddContainer(RunnerName, Image).Resource;
        var node = builder.AddContainer(NodeName, Image).Resource;
        var application = builder.Build();
        try
        {
            return new ComparisonProgressNativeFixture(application, runner, node, progressPath);
        }
        catch (Exception primaryFailure)
        {
            var failures = new List<Exception> { primaryFailure };
            await IsolatedResourceLogCaptureStopSupport.CollectFailureAsync(
                () => application.DisposeAsync().AsTask().WaitAsync(Deadline), failures);
            IsolatedResourceLogCaptureStopSupport.ThrowFailures(failures);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await IsolatedResourceLogCaptureStopSupport.CollectFailureAsync(
            () => Capture.DisposeAsync().AsTask().WaitAsync(Deadline), failures);
        await IsolatedResourceLogCaptureStopSupport.CollectFailureAsync(
            () => Application.DisposeAsync().AsTask().WaitAsync(Deadline), failures);
        IsolatedResourceLogCaptureStopSupport.ThrowFailures(failures);
    }
}
