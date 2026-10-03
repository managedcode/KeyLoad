using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedResourceLogCaptureStopFixture : IAsyncDisposable
{
    private static readonly TimeSpan DisposalDeadline = TimeSpan.FromSeconds(30);

    private IsolatedResourceLogCaptureStopFixture(DistributedApplication application,
        ContainerResource resource, ContainerResource comparisonResource, string resourceName)
    {
        Application = application;
        Resource = resource;
        ComparisonResource = comparisonResource;
        Capture = new ComparisonTestLogCapture(Application, [resourceName]);
    }

    internal DistributedApplication Application { get; }
    internal ComparisonTestLogCapture Capture { get; }
    internal ContainerResource Resource { get; }
    internal ContainerResource ComparisonResource { get; }

    internal static async Task<IsolatedResourceLogCaptureStopFixture> CreateAsync(
        string resourceName, string comparisonResourceName, string containerImage)
    {
        var builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { DisableDashboard = true });
        var resource = builder.AddContainer(resourceName, containerImage).Resource;
        var comparisonResource = builder.AddContainer(comparisonResourceName, containerImage).Resource;
        var application = (DistributedApplication?)null;
        try
        {
            application = builder.Build();
            var fixture = new IsolatedResourceLogCaptureStopFixture(
                application, resource, comparisonResource, resourceName);
            application = null;
            return fixture;
        }
        catch (Exception primaryFailure)
        {
            var failures = new List<Exception> { primaryFailure };
            await DisposeConstructionResourceAsync(application, failures);
            IsolatedResourceLogCaptureStopSupport.ThrowFailures(failures);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        using var captureTimeout = new CancellationTokenSource(DisposalDeadline);
        await IsolatedResourceLogCaptureStopSupport.CollectFailureAsync(
            () => IsolatedResourceLogCaptureStopSupport.AwaitBoundedAndObserveAsync(
                Capture.DisposeAsync().AsTask(), captureTimeout.Token), failures);
        using var applicationTimeout = new CancellationTokenSource(DisposalDeadline);
        await IsolatedResourceLogCaptureStopSupport.CollectFailureAsync(
            () => IsolatedResourceLogCaptureStopSupport.AwaitBoundedAndObserveAsync(
                Application.DisposeAsync().AsTask(), applicationTimeout.Token), failures);
        IsolatedResourceLogCaptureStopSupport.ThrowFailures(failures);
    }

    private static async Task DisposeConstructionResourceAsync(DistributedApplication? resource, List<Exception> failures)
    {
        if (resource is null)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(DisposalDeadline);
        await IsolatedResourceLogCaptureStopSupport.CollectFailureAsync(
            () => IsolatedResourceLogCaptureStopSupport.AwaitBoundedAndObserveAsync(
                resource.DisposeAsync().AsTask(), timeout.Token), failures);
    }
}
