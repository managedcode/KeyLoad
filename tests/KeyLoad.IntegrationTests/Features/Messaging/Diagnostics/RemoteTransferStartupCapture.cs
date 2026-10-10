using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class RemoteTransferStartupCapture : IAsyncDisposable
{
    private const string Missing = "The original six-resource startup observation is invalid.";
    private const int MaximumWindows = 3;
    private const string NoFailure = "None";
    private const string CancelledFailure = "Cancelled";
    private const string DomainFailure = "Domain";
    private const string UnexpectedFailure = "Unexpected";
    private const string ReadyBoundary = "OriginalReadinessReturned";
    private const string TerminalBoundary = "OriginalApplicationStopReturned";
    private const string FailureBoundary = "OriginalStartupFailureObserved";
    private readonly Guid session = Guid.NewGuid();
    private RemoteTransferStartupWindow? window;
    private string primary = NoFailure;
    private ErrorCode? code;
    private bool cancelled;
    private int completedWindows;

    internal RemoteTransferStartupCallbacks Callbacks => new(Attach, ReadyAsync, Failed, JoinAsync);

    private void Attach(DistributedApplication app, ContainerResource[] resources)
    {
        if (window is not null || completedWindows == MaximumWindows || resources.Length != TwoRf3MembershipProtocol.Nodes.Length
            || !resources.Select(resource => resource.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(TwoRf3MembershipProtocol.Nodes.Order(StringComparer.Ordinal), StringComparer.Ordinal))
        { throw new InvalidOperationException(Missing); }
        var logger = app.Services.GetRequiredService<ResourceLoggerService>();
        var notifications = app.ResourceNotifications;
        var buffers = resources.ToDictionary(resource => resource.Name,
            resource => new RemoteTransferStartupBuffer(resource.Name), StringComparer.Ordinal);
        window = new RemoteTransferStartupWindow(logger, notifications, resources, buffers);
    }

    private void Failed(Exception error, bool callerCancelled)
    {
        if (primary != NoFailure)
        { return; }
        primary = error is OperationCanceledException ? CancelledFailure
            : error is KeyLoadException ? DomainFailure : UnexpectedFailure;
        code = (error as KeyLoadException)?.Code;
        cancelled = callerCancelled;
        if (window is { } original)
        { RemoteTransferStartupArtifacts.Save(session, original, FailureBoundary, primary, code, cancelled, error); }
    }

    private Task ReadyAsync() => CloseAsync(ReadyBoundary);
    private Task JoinAsync() => CloseAsync(TerminalBoundary);

    private async Task CloseAsync(string boundary)
    {
        if (window is null)
        { return; }
        var original = window;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => original.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => RemoteTransferStartupArtifacts.Save(session, original, boundary,
            primary, code, cancelled), failures);
        if (failures.Count == 0)
        { window = null; completedWindows++; }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public async ValueTask DisposeAsync()
    {
        if (window is not null)
        { await window.DisposeAsync().ConfigureAwait(false); }
    }
}
