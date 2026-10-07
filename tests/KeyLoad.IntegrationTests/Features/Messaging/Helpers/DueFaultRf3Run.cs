using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class DueFaultRf3Run
{
    internal static async Task ExecuteAsync(CancellationToken executionToken)
    {
        using var deadlineTimeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(executionToken, deadlineTimeout.Token);
        var failures = new List<Exception>();
        string? root = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                root = CreatePrivateRoot();
                var profile = await NodeEpochRf3Profile.CreatePriorAsync(root, deadline.Token).ConfigureAwait(false);
                var images = await RequestCqrsRf3ImageProof.ReadAsync(deadline.Token).ConfigureAwait(false);
                var current = CurrentImages(images.Current);
                var seeded = await DueRf3WaveLifecycle.RunAsync(root, current, async wave =>
                {
                    var seed = await DueFaultRf3SeedWriter.CreateAsync(wave.App, profile.Profile, deadline.Token)
                        .ConfigureAwait(false);
                    var state = await ExerciseLeaderAndRejoinAsync(wave, profile.Profile, seed, deadline.Token)
                        .ConfigureAwait(false);
                    return (Seed: seed, State: state);
                }, deadline.Token).ConfigureAwait(false);
                await VerifyProfileAsync(root, profile.Profile, profile.Bytes, deadline.Token).ConfigureAwait(false);
                await DueRf3WaveLifecycle.RunAsync(root, current, async wave =>
                {
                    await VerifyColdRestartAsync(wave.App, root, seeded.State, profile.Profile, profile.Bytes,
                        seeded.Seed, deadline.Token).ConfigureAwait(false);
                    await ConsumeOutcomesAsync(wave.App, seeded.Seed, deadline.Token)
                        .ConfigureAwait(false);
                    return true;
                }, deadline.Token).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            if (root is not null && failures.Count == 0)
            { ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<DueFaultRf3RestartState> ExerciseLeaderAndRejoinAsync(RequestCqrsRf3Wave wave,
        NodeEpochRf3Profile profile, DueFaultRf3Seed seed, CancellationToken cancellationToken)
    {
        var cut = await DueFaultRf3Leadership.ExerciseLeaderLossAsync(wave.App, wave, profile, seed, cancellationToken)
            .ConfigureAwait(false);
        await DueFaultRf3Leadership.VerifyLeaderRejoinAsync(wave.App, wave, profile, seed, cut, cancellationToken)
            .ConfigureAwait(false);
        var status = await NodeEpochRf3StatusOracle.CaptureAsync(wave.App, profile, cancellationToken).ConfigureAwait(false);
        var discovery = await RequestCqrsRf3DiscoveryOracle.CaptureCurrentAsync(wave.App, profile, cancellationToken)
            .ConfigureAwait(false);
        return new(status, discovery);
    }

    private static async Task VerifyColdRestartAsync(DistributedApplication app, string dataRoot,
        DueFaultRf3RestartState prior, NodeEpochRf3Profile profile, byte[] profileBytes, DueFaultRf3Seed seed,
        CancellationToken cancellationToken)
    {
        await VerifyProfileAsync(dataRoot, profile, profileBytes, cancellationToken)
            .ConfigureAwait(false);
        var status = await NodeEpochRf3StatusOracle.CaptureAsync(app, profile, cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3StatusOracle.AssertSamePhysicalTopologyAsync(prior.Status, status).ConfigureAwait(false);
        foreach (var node in status)
        { await Assert.That(node.Status.Applied).IsGreaterThanOrEqualTo(prior.Status.Single(item => item.Name == node.Name).Status.Applied); }
        var discovery = await RequestCqrsRf3DiscoveryOracle.CaptureCurrentAsync(app, profile, cancellationToken)
            .ConfigureAwait(false);
        await RequestCqrsRf3DiscoveryOracle.AssertReplacementAsync(prior.Discovery, discovery).ConfigureAwait(false);
        await using var first = await NodeEpochRf3Callers.ConnectAsync(app,
            RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node1, seed.Creator.Secret, cancellationToken)
            .ConfigureAwait(false);
        await using var second = await NodeEpochRf3Callers.ConnectAsync(app,
            RequestCqrsRf3Protocol.Node3, RequestCqrsRf3Protocol.Node3, seed.Creator.Secret, cancellationToken)
            .ConfigureAwait(false);
        await DueFaultRf3Assertions.AssertOutcomesAsync(first, second, seed, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ConsumeOutcomesAsync(DistributedApplication app,
        DueFaultRf3Seed seed, CancellationToken cancellationToken)
    {
        await using var callers = await NodeEpochRf3Callers.ConnectAsync(app,
            RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2, seed.Creator.Secret, cancellationToken)
            .ConfigureAwait(false);
        await DueFaultRf3Assertions.AcknowledgeAndProveExhaustionAsync(callers, seed, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task VerifyProfileAsync(string root, NodeEpochRf3Profile profile, byte[] expected,
        CancellationToken cancellationToken)
    {
        var file = Path.Combine(root, NodeEpochRf3Protocol.ProfileFile);
        var actual = await NodeEpochRf3Profile.ReadAsync(file, cancellationToken).ConfigureAwait(false);
        await Assert.That(actual.Bytes.AsSpan().SequenceEqual(expected)).IsTrue();
        await Assert.That(actual.Profile.Incarnation).IsEqualTo(profile.Incarnation);
    }

    private static string CreatePrivateRoot()
    {
        var parent = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            "artifacts", "qualification");
        Directory.CreateDirectory(parent);
        var root = Path.Combine(parent, "due-fault-rf3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        return root;
    }

    private static Dictionary<string, string> CurrentImages(string image)
        => new(StringComparer.Ordinal)
        {
            [RequestCqrsRf3Protocol.Node1] = image,
            [RequestCqrsRf3Protocol.Node2] = image,
            [RequestCqrsRf3Protocol.Node3] = image
        };
}
