using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class RequestCqrsRf3FollowerRestartTests
{
    [Test]
    public async Task TwoCompatibleVotersServeAndRestartedFollowerPublishesANewSignedGeneration()
    {
        using var deadlineTimeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, deadlineTimeout.Token);
        var dataRoot = CreatePrivateRoot();
        var failures = new List<Exception>();
        LocalRf3ImageTestSession? localImageSession = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var (profile, _) = await NodeEpochRf3Profile.CreatePriorAsync(dataRoot, deadline.Token).ConfigureAwait(false);
            localImageSession = await LocalRf3ImageTestSession.StartIfSelectedAsync(deadline.Token).ConfigureAwait(false);
            var images = await RequestCqrsRf3ImageProof.ReadAsync(deadline.Token, localImageSession?.Selection).ConfigureAwait(false);
            var currentReference = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RequestCqrsRf3Protocol.Node1] = images.Current,
                [RequestCqrsRf3Protocol.Node2] = images.Current,
                [RequestCqrsRf3Protocol.Node3] = images.Current
            };
            var run = new RequestCqrsRf3FollowerRun(dataRoot, profile, deadline.Token);
            var preparation = localImageSession;
            localImageSession = null;
            await run.ExecuteAsync(currentReference, preparation).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (localImageSession is { } preparationBeforeTransfer)
        {
            await ClusterFixtureLocalImageLifecycle.JoinAsync(preparationBeforeTransfer, dataRoot,
                stopped: false, neverBuilt: true, failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        Directory.Delete(dataRoot, recursive: true);
    }

    private static string CreatePrivateRoot()
    {
        var root = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            "artifacts", "qualification", "cluster-routing-c1-follower-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return root;
    }
}
