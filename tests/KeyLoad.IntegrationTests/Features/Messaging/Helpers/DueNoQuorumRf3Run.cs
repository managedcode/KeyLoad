using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class DueNoQuorumRf3Run
{
    internal static async Task ExecuteAsync(CancellationToken executionToken)
    {
        using var deadlineTimeout = new CancellationTokenSource(DueNoQuorumRf3Protocol.ParentDeadline, TimeProvider.System);
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
                await DueRf3WaveLifecycle.RunAsync(root, CurrentImages(images.Current), async wave =>
                {
                    var seed = await DueNoQuorumRf3SeedWriter.CreateAsync(wave.App, profile.Profile, deadline.Token)
                        .ConfigureAwait(false);
                    await ExerciseNoQuorumAndRecoveryAsync(wave, profile.Profile, seed, deadline.Token)
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

    private static async Task ExerciseNoQuorumAndRecoveryAsync(RequestCqrsRf3Wave wave,
        NodeEpochRf3Profile profile, DueNoQuorumRf3Seed seed, CancellationToken cancellationToken)
    {
        await using (var initial = await DueNoQuorumRf3Callers.ConnectAsync(wave.App,
            RequestCqrsRf3Protocol.Node1, seed.Creator.Secret, cancellationToken).ConfigureAwait(false))
        {
            await DueNoQuorumRf3Assertions.AssertOrdinalAsync(initial, seed, 0, cancellationToken).ConfigureAwait(false);
            await DueNoQuorumRf3Assertions.AssertOccurrenceAsync(initial, seed, expectedPresent: false, cancellationToken)
                .ConfigureAwait(false);
        }
        var plan = await DueNoQuorumRf3Topology.CaptureAsync(wave.App, profile, seed.DueAt, cancellationToken)
            .ConfigureAwait(false);
        await KillBeforeDueAsync(wave, plan, seed.DueAt, cancellationToken).ConfigureAwait(false);
        await WaitThroughDueWindowAsync(seed.DueAt, cancellationToken).ConfigureAwait(false);
        await VerifySingleVoterUnavailableAsync(wave.App, plan, seed, cancellationToken).ConfigureAwait(false);
        await wave.RestartAsync(plan.RecoveryNode, cancellationToken).ConfigureAwait(false);
        await DueNoQuorumRf3Topology.AssertOneRestartedAsync(wave.App, profile, plan, cancellationToken)
            .ConfigureAwait(false);
        var minimumApplied = await VerifyAutonomousResumeAsync(wave.App, profile, plan, seed, cancellationToken).ConfigureAwait(false);
        await wave.RestartAsync(plan.Leader, cancellationToken).ConfigureAwait(false);
        await VerifyThreeVoterCatchupAsync(wave.App, profile, plan, seed, minimumApplied, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task KillBeforeDueAsync(RequestCqrsRf3Wave wave, DueNoQuorumRf3FaultPlan plan,
        DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        await wave.KillDueNoQuorumAsync(plan.Leader, cancellationToken).ConfigureAwait(false);
        RequireBeforeDue(dueAt);
        await wave.KillDueNoQuorumAsync(plan.RecoveryNode, cancellationToken).ConfigureAwait(false);
        RequireBeforeDue(dueAt);
    }

    private static async Task VerifySingleVoterUnavailableAsync(Aspire.Hosting.DistributedApplication app,
        DueNoQuorumRf3FaultPlan plan, DueNoQuorumRf3Seed seed, CancellationToken cancellationToken)
    {
        await DueNoQuorumRf3Assertions.AssertNoQuorumAsync(app, plan.Survivor, seed, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<long> VerifyAutonomousResumeAsync(Aspire.Hosting.DistributedApplication app,
        NodeEpochRf3Profile profile, DueNoQuorumRf3FaultPlan plan, DueNoQuorumRf3Seed seed,
        CancellationToken cancellationToken)
    {
        await using var restored = await DueNoQuorumRf3Callers.ConnectAsync(app, plan.RecoveryNode,
            seed.Creator.Secret, cancellationToken).ConfigureAwait(false);
        await DueNoQuorumRf3Assertions.WaitForOrdinalOneAsync(restored, seed, cancellationToken).ConfigureAwait(false);
        using var recoveryHttp = McpCallerHttp.Create(app, plan.RecoveryNode);
        var recoveryAdmin = new KeyLoad.Client.KeyLoadClient(recoveryHttp, profile.AdminKey, IntegrationClientOptions.Execution());
        var status = await McpCallerAssertions.SdkSuccessAsync(await recoveryAdmin.StatusAsync(cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        using var survivorHttp = McpCallerHttp.Create(app, plan.Survivor);
        var survivorSdk = new KeyLoad.Client.KeyLoadClient(survivorHttp, profile.AdminKey, IntegrationClientOptions.Execution());
        var survivorStatus = await McpCallerAssertions.SdkSuccessAsync(await survivorSdk.StatusAsync(cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(status.RoutingReady).IsTrue();
        await Assert.That(survivorStatus.RoutingReady).IsTrue();
        await Assert.That(status.Voters).IsEqualTo(RequestCqrsRf3Protocol.NodeCount);
        await Assert.That(survivorStatus.Voters).IsEqualTo(RequestCqrsRf3Protocol.NodeCount);
        await Assert.That(status.Leader).IsEqualTo(survivorStatus.Leader);
        return Math.Max(status.Applied, survivorStatus.Applied);
    }

    private static async Task VerifyThreeVoterCatchupAsync(Aspire.Hosting.DistributedApplication app,
        NodeEpochRf3Profile profile, DueNoQuorumRf3FaultPlan plan, DueNoQuorumRf3Seed seed,
        long minimumApplied, CancellationToken cancellationToken)
    {
        await NodeEpochRf3StatusOracle.EventuallyCaughtUpAsync(app, profile, minimumApplied, cancellationToken)
            .ConfigureAwait(false);
        var status = await NodeEpochRf3StatusOracle.CaptureAsync(app, profile, cancellationToken).ConfigureAwait(false);
        foreach (var node in status)
        { await Assert.That(node.Status.Applied).IsGreaterThanOrEqualTo(minimumApplied); }
        await DueNoQuorumRf3Topology.AssertFinalDiscoveryAsync(app, profile, plan, cancellationToken)
            .ConfigureAwait(false);
        foreach (var node in new[] { RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2,
                     RequestCqrsRf3Protocol.Node3 })
        {
            await using var callers = await DueNoQuorumRf3Callers.ConnectAsync(app, node,
                seed.Creator.Secret, cancellationToken).ConfigureAwait(false);
            await DueNoQuorumRf3Assertions.AssertRecoveredClientsAsync(callers, seed, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task WaitThroughDueWindowAsync(DateTimeOffset dueAt, CancellationToken cancellationToken)
    {
        var observationEnd = dueAt.AddSeconds(DueNoQuorumRf3Protocol.NoQuorumObservationSeconds);
        var remaining = observationEnd - TimeProvider.System.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        { await Task.Delay(remaining, TimeProvider.System, cancellationToken).ConfigureAwait(false); }
    }

    private static void RequireBeforeDue(DateTimeOffset dueAt)
    {
        if (TimeProvider.System.GetUtcNow() >= dueAt)
        { throw new TimeoutException(DueNoQuorumRf3Protocol.SetupFailure); }
    }

    private static string CreatePrivateRoot()
    {
        var parent = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            "artifacts", "qualification");
        Directory.CreateDirectory(parent);
        var root = Path.Combine(parent, "due-no-quorum-rf3-" + Guid.NewGuid().ToString("N"));
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
