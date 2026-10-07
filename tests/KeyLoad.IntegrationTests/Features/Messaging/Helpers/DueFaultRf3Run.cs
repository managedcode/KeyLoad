using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;
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
        var lifecycle = new RequestCqrsLifecycleEvidence();
        lifecycle.SetTokens(executionToken, deadline.Token, default);
        string? root = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                root = CreatePrivateRoot();
                lifecycle.SetStage(RequestCqrsLifecycleStage.FaultProfile);
                var profile = await NodeEpochRf3Profile.CreatePriorAsync(root, deadline.Token).ConfigureAwait(false);
                lifecycle.SetStage(RequestCqrsLifecycleStage.FaultImages);
                var images = await RequestCqrsRf3ImageProof.ReadAsync(deadline.Token).ConfigureAwait(false);
                var current = CurrentImages(images.Current);
                lifecycle.SetScenarioPhase(RequestCqrsLifecycleStage.DueOriginalWave);
                var seeded = await DueRf3WaveLifecycle.RunAsync(root, current, async wave =>
                {
                    lifecycle.SetStage(RequestCqrsLifecycleStage.DueSeed);
                    var seed = await DueFaultRf3SeedWriter.CreateAsync(wave.App, profile.Profile, deadline.Token)
                        .ConfigureAwait(false);
                    var state = await ExerciseLeaderAndRejoinAsync(wave, profile.Profile, seed, lifecycle, deadline.Token)
                        .ConfigureAwait(false);
                    return (Seed: seed, State: state);
                }, deadline.Token, lifecycle).ConfigureAwait(false);
                lifecycle.SetStage(RequestCqrsLifecycleStage.FaultProfile);
                await VerifyProfileAsync(root, profile.Profile, profile.Bytes, deadline.Token).ConfigureAwait(false);
                lifecycle.SetScenarioPhase(RequestCqrsLifecycleStage.DueColdWave);
                await DueRf3WaveLifecycle.RunAsync(root, current, async wave =>
                {
                    await VerifyColdRestartAsync(wave.App, root, seeded.State, profile.Profile, profile.Bytes,
                        seeded.Seed, lifecycle, deadline.Token).ConfigureAwait(false);
                    await ConsumeOutcomesAsync(wave.App, seeded.Seed, lifecycle, deadline.Token)
                        .ConfigureAwait(false);
                    return true;
                }, deadline.Token, lifecycle).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            lifecycle.RecordFirstFailureIfAny(failures);
            if (root is not null && failures.Count == 0)
            { ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures); }
        }
        lifecycle.RecordTerminal();
        lifecycle.ThrowWithContext(failures);
    }

    private static async Task<DueFaultRf3RestartState> ExerciseLeaderAndRejoinAsync(RequestCqrsRf3Wave wave,
        NodeEpochRf3Profile profile, DueFaultRf3Seed seed, RequestCqrsLifecycleEvidence lifecycle, CancellationToken cancellationToken)
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.DueLeaderLoss);
        var cut = await DueFaultRf3Leadership.ExerciseLeaderLossAsync(wave.App, wave, profile, seed, cancellationToken)
            .ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.DueLeaderRejoin);
        await DueFaultRf3Leadership.VerifyLeaderRejoinAsync(wave.App, wave, profile, seed, cut, cancellationToken)
            .ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.DueStatus);
        var status = await NodeEpochRf3StatusOracle.CaptureAsync(wave.App, profile, cancellationToken).ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.DueDiscovery);
        var discovery = await RequestCqrsRf3DiscoveryOracle.CaptureCurrentAsync(wave.App, profile, cancellationToken)
            .ConfigureAwait(false);
        return new(status, discovery);
    }

    private static async Task VerifyColdRestartAsync(DistributedApplication app, string dataRoot,
        DueFaultRf3RestartState prior, NodeEpochRf3Profile profile, byte[] profileBytes, DueFaultRf3Seed seed,
        RequestCqrsLifecycleEvidence lifecycle, CancellationToken cancellationToken)
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.FaultProfile);
        await VerifyProfileAsync(dataRoot, profile, profileBytes, cancellationToken)
            .ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.DueStatus);
        var status = await NodeEpochRf3StatusOracle.CaptureAsync(app, profile, cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3StatusOracle.AssertSamePhysicalTopologyAsync(prior.Status, status).ConfigureAwait(false);
        foreach (var node in status)
        { await Assert.That(node.Status.Applied).IsGreaterThanOrEqualTo(prior.Status.Single(item => item.Name == node.Name).Status.Applied); }
        lifecycle.SetStage(RequestCqrsLifecycleStage.DueDiscovery);
        var discovery = await RequestCqrsRf3DiscoveryOracle.CaptureCurrentAsync(app, profile, cancellationToken)
            .ConfigureAwait(false);
        await RequestCqrsRf3DiscoveryOracle.AssertReplacementAsync(prior.Discovery, discovery).ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.FaultCaller);
        await using var first = await NodeEpochRf3Callers.ConnectAsync(app,
            RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node1, seed.Creator.Secret, cancellationToken)
            .ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.FaultCaller);
        await using var second = await NodeEpochRf3Callers.ConnectAsync(app,
            RequestCqrsRf3Protocol.Node3, RequestCqrsRf3Protocol.Node3, seed.Creator.Secret, cancellationToken)
            .ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.DueOutcomes);
        await DueFaultRf3Assertions.AssertOutcomesAsync(first, second, seed, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ConsumeOutcomesAsync(DistributedApplication app,
        DueFaultRf3Seed seed, RequestCqrsLifecycleEvidence lifecycle, CancellationToken cancellationToken)
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.FaultCaller);
        await using var callers = await NodeEpochRf3Callers.ConnectAsync(app,
            RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2, seed.Creator.Secret, cancellationToken)
            .ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.DueConsume);
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
