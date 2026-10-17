using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal sealed class NativeCapabilityStartupCapture(Action<Exception, bool> originalFailure) : IAsyncDisposable
{
    private readonly RemoteTransferStartupCapture capture = new();
    private bool attached;
    private bool readinessReturned;

    internal RemoteTransferStartupCallbacks Callbacks => new(Attach, ReadyAsync, Failed, JoinAsync);

    private void Attach(DistributedApplication application, ContainerResource[] resources)
    {
        if (attached)
        {
            return;
        }
        capture.Callbacks.Attach(application, resources);
        attached = true;
    }

    private async Task ReadyAsync()
    {
        if (readinessReturned)
        {
            return;
        }
        await capture.Callbacks.Ready().ConfigureAwait(false);
        readinessReturned = true;
    }

    private void Failed(Exception failure, bool cancelled)
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => originalFailure(failure, cancelled), failures);
        if (!readinessReturned)
        {
            ServerFailureObserver.Observe(() => capture.Callbacks.Failed(failure, cancelled), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private Task JoinAsync() => capture.Callbacks.Join();

    public async ValueTask DisposeAsync()
        => await capture.DisposeAsync().ConfigureAwait(false);
}
