using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class TwoRf3MembershipWave : IAsyncDisposable
{
    private DistributedApplication? application;
    private string? dataRoot;
    private bool dataRootOwned;
    private bool startAttempted;
    private bool applicationDisposed;
    private bool nodeLocksReleased;
    private bool cleanupFailed;

    internal NodeEpochRf3Profile Profile
    {
        get => field
        ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState); private set;
    }
    internal DistributedApplication Application => application
        ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);

    internal static async Task<TwoRf3MembershipWave> StartAsync(CancellationToken cancellationToken)
    {
        var wave = new TwoRf3MembershipWave();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => wave.StartCoreAsync(cancellationToken), failures)
            .ConfigureAwait(false);
        if (failures.Count == 0)
        { return wave; }
        await ServerFailureObserver.ObserveAsync(() => wave.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        var root = CreateOwnedRoot();
        Profile = (await NodeEpochRf3Profile.CreatePriorAsync(root, cancellationToken).ConfigureAwait(false)).Profile;
        var reference = await ClusterFixtureImageIdentity.ReadVerifiedReferenceAsync(cancellationToken).ConfigureAwait(false);
        var args = new[] { TwoRf3MembershipProtocol.DataRootPrefix + root,
            TwoRf3MembershipProtocol.EphemeralArgument, TwoRf3MembershipProtocol.ProfileArgument };
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(args,
            cancellationToken).ConfigureAwait(false);
        application = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
        await TwoRf3MembershipImageAssertions.VerifyAsync(application, reference, cancellationToken).ConfigureAwait(false);
        startAttempted = true;
        await application.StartAsync(cancellationToken).ConfigureAwait(false);
        await WaitForSixHealthyAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task WaitForSixHealthyAsync(CancellationToken cancellationToken)
    {
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            await Application.ResourceNotifications.WaitForResourceHealthyAsync(node, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await DisposeApplicationAsync(failures).ConfigureAwait(false);
        if (CanCheckLocks)
        { ServerFailureObserver.Observe(AssertAllNodeLocksReleased, failures); }
        if (CanDeleteRoot(failures))
        {
            var root = dataRoot!;
            ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures);
            if (failures.Count == 0)
            { dataRoot = null; dataRootOwned = false; }
        }
        cleanupFailed |= failures.Count > 0;
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task DisposeApplicationAsync(List<Exception> failures)
    {
        var owned = application;
        if (owned is null)
        { return; }
        var before = failures.Count;
        using var deadline = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline);
        await ServerFailureObserver.ObserveAsync(() => owned.StopAsync(deadline.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        if (failures.Count == before)
        { application = null; applicationDisposed = true; }
        else
        { cleanupFailed = true; }
    }

    private bool CanCheckLocks => startAttempted && applicationDisposed && !nodeLocksReleased
        && !cleanupFailed && dataRoot is not null;

    private bool CanDeleteRoot(List<Exception> failures)
        => dataRootOwned && dataRoot is not null && failures.Count == 0 && !cleanupFailed
            && (!startAttempted || nodeLocksReleased) && (application is null || applicationDisposed);

    private void AssertAllNodeLocksReleased()
    {
        var root = dataRoot ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.InvalidRoot);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, node, "node.owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, node, "database", "owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, node, "replica", "owner.lock"));
        }
        nodeLocksReleased = true;
    }

    private string CreateOwnedRoot()
    {
        var path = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName, "artifacts",
            "qualification", "membership-stage1a-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        dataRoot = path;
        dataRootOwned = true;
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        return path;
    }
}
