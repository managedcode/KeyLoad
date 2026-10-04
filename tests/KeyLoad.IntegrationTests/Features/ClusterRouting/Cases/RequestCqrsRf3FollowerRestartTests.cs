using Aspire.Hosting;
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
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(RequestCqrsRf3Protocol.ParentDeadline);
        var dataRoot = CreatePrivateRoot();
        var (profile, _) = await NodeEpochRf3Profile.CreatePriorAsync(dataRoot, deadline.Token).ConfigureAwait(false);
        var images = await RequestCqrsRf3ImageProof.ReadAsync(deadline.Token).ConfigureAwait(false);
        var currentReference = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RequestCqrsRf3Protocol.Node1] = images.Current,
            [RequestCqrsRf3Protocol.Node2] = images.Current,
            [RequestCqrsRf3Protocol.Node3] = images.Current
        };
        var run = new RequestCqrsRf3FollowerRun(dataRoot, profile, deadline.Token);
        await run.ExecuteAsync(currentReference).ConfigureAwait(false);
        Directory.Delete(dataRoot, recursive: true);
    }

    private static string CreatePrivateRoot()
    {
        var root = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            "artifacts", "qualification", "cluster-routing-c1-follower-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        return root;
    }
}

internal sealed class RequestCqrsRf3FollowerRun(string dataRoot, NodeEpochRf3Profile profile,
    CancellationToken cancellationToken)
{
    internal async Task ExecuteAsync(IReadOnlyDictionary<string, string> images)
    {
        var failures = new List<Exception>();
        try
        {
            await using var wave = await RequestCqrsRf3Wave.StartAsync(dataRoot, images, false, true, cancellationToken)
                .ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(() => RunInWaveAsync(wave), failures).ConfigureAwait(false);
        }
        catch (Exception error) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task RunInWaveAsync(RequestCqrsRf3Wave wave)
    {
        var workload = await RequestCqrsRf3Workload.SeedAsync(wave.App, profile, cancellationToken)
            .ConfigureAwait(false);
        var originalStatus = await NodeEpochRf3StatusOracle.CaptureAsync(wave.App, profile, cancellationToken)
            .ConfigureAwait(false);
        var originalDiscovery = await RequestCqrsRf3DiscoveryOracle.CaptureCurrentAsync(wave.App, profile,
            cancellationToken).ConfigureAwait(false);
        var follower = FindFollower(originalStatus);
        await wave.KillAsync(follower.Name, cancellationToken).ConfigureAwait(false);
        var survivor = FirstSurvivor(originalStatus, follower.Name);
        await RequestCqrsRf3SurvivorOracle.VerifyAsync(wave.App, profile, workload, originalStatus,
            follower.Name, cancellationToken).ConfigureAwait(false);
        var receipt = await workload.AppendCurrentWriteAsync(wave.App, profile, survivor.Name, cancellationToken)
            .ConfigureAwait(false);
        await workload.VerifyCurrentWriteAsync(wave.App, profile, survivor.Name, receipt, cancellationToken)
            .ConfigureAwait(false);
        await wave.RestartAsync(follower.Name, cancellationToken).ConfigureAwait(false);
        await VerifyRestartedGenerationAsync(wave.App, originalDiscovery, follower.Name)
            .ConfigureAwait(false);
        await NodeEpochRf3StatusOracle.EventuallyCaughtUpAsync(wave.App, profile, receipt.Token.Position, cancellationToken)
            .ConfigureAwait(false);
        var restored = await NodeEpochRf3StatusOracle.CaptureAsync(wave.App, profile, cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3StatusOracle.AssertSamePhysicalTopologyAsync(originalStatus, restored).ConfigureAwait(false);
        foreach (var node in restored)
        { await Assert.That(node.Status.Applied).IsGreaterThanOrEqualTo(receipt.Token.Position); }
        await workload.VerifyPreservedAsync(wave.App, profile, cancellationToken, currentWriteExists: true)
            .ConfigureAwait(false);
        await workload.VerifyCurrentWriteAsync(wave.App, profile, follower.Name, receipt, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task VerifyRestartedGenerationAsync(DistributedApplication app,
        KeyLoad.Orleans.ReplicaSiloDiscovery[] before, string follower)
    {
        var after = await RequestCqrsRf3DiscoveryOracle.CaptureCurrentAsync(app, profile, cancellationToken)
            .ConfigureAwait(false);
        var index = Array.FindIndex(before, record => record.VoterId == VoterOrigin(follower));
        if (index < 0)
        { throw new InvalidOperationException("The restarted voter was absent from the original signed RF3 topology."); }
        await RequestCqrsRf3DiscoveryOracle.AssertOneReplacementAsync(before, after, index).ConfigureAwait(false);
    }

    private static NodeEpochRf3NodeObservation FindFollower(IReadOnlyList<NodeEpochRf3NodeObservation> nodes)
    {
        var leaders = nodes.Select(node => node.Status.Leader).Where(value => value is not null)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (leaders.Length != 1)
        { throw new InvalidOperationException("The healthy RF3 topology did not report one actual leader."); }
        return nodes.First(node => node.Admin.LocalVoter != leaders[0]);
    }

    private static NodeEpochRf3NodeObservation FirstSurvivor(
        IReadOnlyList<NodeEpochRf3NodeObservation> nodes, string follower)
        => nodes.First(node => node.Name != follower);

    private static string VoterOrigin(string node) => node switch
    {
        RequestCqrsRf3Protocol.Node1 => "http://node1:8080",
        RequestCqrsRf3Protocol.Node2 => "http://node2:8080",
        RequestCqrsRf3Protocol.Node3 => "http://node3:8080",
        _ => throw new ArgumentOutOfRangeException(nameof(node))
    };
}
