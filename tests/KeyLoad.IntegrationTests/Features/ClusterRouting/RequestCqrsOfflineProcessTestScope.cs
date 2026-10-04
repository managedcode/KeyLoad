using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class RequestCqrsOfflineProcessTestScope : IAsyncDisposable
{
    private readonly object disposalGate = new();
    private readonly string root;
    private readonly CancellationToken cancellationToken;
    private readonly List<RequestCqrsOfflineProcessOwnedChild> children = [];
    private Task? disposalTask;

    private RequestCqrsOfflineProcessTestScope(string root, CancellationToken cancellationToken)
    { this.root = root; this.cancellationToken = cancellationToken; }

    internal static async Task RunAsync(Func<RequestCqrsOfflineProcessTestScope, Task> test,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        try
        {
            await using var scope = new RequestCqrsOfflineProcessTestScope(CreatePrivateRoot(), cancellationToken);
            await ServerFailureObserver.ObserveAsync(() => test(scope), failures).ConfigureAwait(false);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal async Task<NodeEpochRf3OfflineResult> StartSuccessAsync()
        => await Add(RequestCqrsOfflineProcessProtocol.StartSuccess()).ObserveResultAsync().ConfigureAwait(false);

    internal RequestCqrsOfflineProcessChildView StartCancellation()
        => Add(RequestCqrsOfflineProcessProtocol.StartWaitingChild(
            Path.Combine(root, Guid.NewGuid().ToString("N") + ".pid"), Guid.NewGuid().ToString("N"), false)).Borrow();

    internal RequestCqrsOfflineProcessChildView StartOversized()
    {
        var nonce = Guid.NewGuid().ToString("N");
        return Add(RequestCqrsOfflineProcessProtocol.StartWaitingChild(
            Path.Combine(root, nonce + ".pid"), nonce, true, Path.Combine(root, nonce + ".release"))).Borrow();
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalGate)
        {
            disposalTask ??= DisposeCoreAsync();
            return new(disposalTask);
        }
    }

    private RequestCqrsOfflineProcessOwnedChild Add(System.Diagnostics.ProcessStartInfo start)
    {
        var child = new RequestCqrsOfflineProcessOwnedChild(start, cancellationToken);
        children.Add(child);
        return child;
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        foreach (var child in children)
        { await ServerFailureObserver.ObserveAsync(() => child.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (children.All(child => child.IsSettled))
        { ServerFailureObserver.Observe(DeleteRoot, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static string CreatePrivateRoot()
    {
        var parent = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            "artifacts", "qualification");
        Directory.CreateDirectory(parent);
        var root = Path.Combine(parent, "cluster-routing-offline-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        return root;
    }

    private void DeleteRoot() => Directory.Delete(root, recursive: true);
}
