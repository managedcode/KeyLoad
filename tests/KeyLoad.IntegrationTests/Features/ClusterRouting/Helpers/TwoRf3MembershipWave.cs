using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
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
    private readonly LocalRf3ImageSelection.Selection? localImageSelection;
    private LocalRf3ImageIdentity.Identity? localImageIdentity;

    private TwoRf3MembershipWave(LocalRf3ImageSelection.Selection? selection)
        => localImageSelection = selection;

    internal NodeEpochRf3Profile Profile
    {
        get => field
        ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState); private set;
    }
    internal DistributedApplication Application => application
        ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);

    internal static Task<TwoRf3MembershipWave> StartAsync(CancellationToken cancellationToken)
        => StartAsync(null, cancellationToken);

    internal static async Task<TwoRf3MembershipWave> StartAsync(LocalRf3ImageSelection.Selection? selection,
        CancellationToken cancellationToken)
    {
        var wave = new TwoRf3MembershipWave(selection);
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
        var repository = ClusterFixtureDiagnostics.FindRepositoryRoot().FullName;
        string? githubReference = null;
        if (localImageSelection is null)
        {
            githubReference = await ClusterFixtureImageIdentity.ReadVerifiedReferenceAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            _ = await LocalRf3ImageIdentity.ReadVerifiedAsync(repository, localImageSelection, cancellationToken)
                .ConfigureAwait(false) ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.ImageMismatch);
        }
        var args = CreateArguments(root, localImageSelection);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(args,
            cancellationToken).ConfigureAwait(false);
        var containerNames = ReadContainerNames(builder.Resources.OfType<ContainerResource>());
        application = await builder.BuildAsync(cancellationToken).ConfigureAwait(false);
        if (localImageSelection is null)
        {
            await TwoRf3MembershipImageAssertions.VerifyAsync(application,
                    githubReference ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.ImageMismatch),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            localImageIdentity = await LocalRf3ImageIdentity.VerifyBeforeStartAsync(application, repository,
                localImageSelection, TwoRf3MembershipProtocol.Nodes, cancellationToken).ConfigureAwait(false);
        }
        startAttempted = true;
        await application.StartAsync(cancellationToken).ConfigureAwait(false);
        await WaitForSixHealthyAsync(cancellationToken).ConfigureAwait(false);
        if (localImageIdentity is { } identity)
        {
            await LocalRf3ImageIdentity.VerifyStartedContainersAsync(identity, containerNames,
                TwoRf3MembershipProtocol.Nodes, cancellationToken).ConfigureAwait(false);
        }
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
        using var deadline = new CancellationTokenSource(TwoRf3MembershipProtocol.CleanupDeadline, TimeProvider.System);
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

    private static string[] CreateArguments(string root, LocalRf3ImageSelection.Selection? selection)
    {
        var args = new List<string>
        {
            TwoRf3MembershipProtocol.DataRootPrefix + root,
            TwoRf3MembershipProtocol.EphemeralArgument,
            TwoRf3MembershipProtocol.ProfileArgument
        };
        if (selection is not null)
        {
            args.AddRange(selection.CreateWaveArguments());
        }
        return [.. args];
    }

    private static Dictionary<string, string> ReadContainerNames(IEnumerable<ContainerResource> resources)
    {
        var names = resources.Where(resource => TwoRf3MembershipProtocol.Nodes.Contains(resource.Name,
                StringComparer.Ordinal))
            .ToDictionary(resource => resource.Name,
                resource => resource.Annotations.OfType<ContainerNameAnnotation>().Single().Name,
                StringComparer.Ordinal);
        if (names.Count != TwoRf3MembershipProtocol.NodeCount
            || TwoRf3MembershipProtocol.Nodes.Any(node => !names.ContainsKey(node)))
        {
            throw new InvalidOperationException(TwoRf3MembershipProtocol.ImageMismatch);
        }
        return names;
    }

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
