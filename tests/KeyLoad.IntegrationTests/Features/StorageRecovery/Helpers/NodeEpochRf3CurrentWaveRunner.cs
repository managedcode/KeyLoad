using System.Collections.Immutable;
using Aspire.Hosting;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NodeEpochRf3CurrentWaveRunner
{
    private const string FollowerLossScenario = "storage-recovery-node-epoch-cold-follower-loss";

    internal static async Task RunAsync(NodeEpochRf3TrialRoots roots, List<Exception> failures,
        Action<NodeEpochRf3Wave> retainWave, CancellationToken cancellationToken)
    {
        var migration = new NodeEpochRf3Migration(roots.Prior, roots.Current, roots.Profile!, roots.PriorNodes!);
        await PrepareTargetsAsync(roots, migration, cancellationToken).ConfigureAwait(false);
        await VerifyCurrentProfileAsync(roots, cancellationToken).ConfigureAwait(false);
        await using var wave = await NodeEpochRf3Wave.StartAsync(roots.Current, null, cancellationToken).ConfigureAwait(false);
        retainWave(wave);
        await ServerFailureObserver.ObserveAsync(() => RunCurrentDataAsync(roots, migration, wave, cancellationToken),
            failures).ConfigureAwait(false);
    }

    internal static async Task PrepareTargetsAsync(NodeEpochRf3TrialRoots roots, NodeEpochRf3Migration migration,
        CancellationToken cancellationToken)
    {
        var sourceInventories = await migration.CaptureSourcesAsync(cancellationToken).ConfigureAwait(false);
        var copiedProfileSha = await roots.Profile!.CopyExactAsync(roots.Current, roots.ProfileBytes!, cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(copiedProfileSha).IsEqualTo(roots.ProfileSha256);
        await migration.VerifyOriginalProfileAsync(roots.ProfileBytes!, roots.ProfileSha256!, cancellationToken)
            .ConfigureAwait(false);
        await migration.VerifyInvalidThirdBarrierAsync(roots.Negative, cancellationToken).ConfigureAwait(false);
        await migration.PrepareAndVerifyAllAsync(cancellationToken).ConfigureAwait(false);
        await migration.PublishAllWithBarrierAsync(cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3MigrationAssertions.AssertOriginalInventoriesAsync(roots.Prior, sourceInventories,
            cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task VerifyCurrentProfileAsync(NodeEpochRf3TrialRoots roots,
        CancellationToken cancellationToken)
    {
        var current = await NodeEpochRf3Profile.ReadAsync(Path.Combine(roots.Current,
            NodeEpochRf3Protocol.ProfileFile), cancellationToken).ConfigureAwait(false);
        await Assert.That(current.Bytes.AsSpan().SequenceEqual(roots.ProfileBytes!)).IsTrue();
        await Assert.That(current.Sha256).IsEqualTo(roots.ProfileSha256);
        await Assert.That(current.Profile.Incarnation).IsEqualTo(roots.Profile!.Incarnation);
    }

    private static async Task RunCurrentDataAsync(NodeEpochRf3TrialRoots roots, NodeEpochRf3Migration migration,
        NodeEpochRf3Wave wave, CancellationToken cancellationToken)
    {
        var profile = roots.Profile!;
        var currentNodes = await NodeEpochRf3StatusOracle.CaptureAsync(wave.App, profile, cancellationToken)
            .ConfigureAwait(false);
        await NodeEpochRf3StatusOracle.AssertSamePhysicalTopologyAsync(roots.PriorNodes!, currentNodes)
            .ConfigureAwait(false);
        var expected = await ApplyAndVerifyCurrentWritesAsync(wave.App, profile, roots.Workload!, cancellationToken)
            .ConfigureAwait(false);
        var afterWrites = await NodeEpochRf3StatusOracle.CaptureAsync(wave.App, profile, cancellationToken)
            .ConfigureAwait(false);
        var follower = Follower(afterWrites);
        await VerifyRevokedFollowerRestartAsync(roots, wave, profile, expected, afterWrites, follower.Name,
            cancellationToken).ConfigureAwait(false);
        await wave.StopAsync(cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3CurrentSnapshotAssertions.VerifyAllAsync(roots.Prior, roots.Current,
            profile, migration, cancellationToken).ConfigureAwait(false);
        await migration.VerifyPublishedRetryPreservesLaterWritesAsync(cancellationToken).ConfigureAwait(false);
        await migration.VerifyOriginalProfileAsync(roots.ProfileBytes!, roots.ProfileSha256!, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task VerifyRevokedFollowerRestartAsync(NodeEpochRf3TrialRoots roots,
        NodeEpochRf3Wave wave, NodeEpochRf3Profile profile, ImmutableArray<NodeEpochRf3ExpectedSample> expected,
        NodeEpochRf3NodeObservation[] beforeLoss, string follower, CancellationToken cancellationToken)
    {
        await using var reader = await NodeEpochRf3Callers.ConnectAsync(wave.App, follower, follower,
            roots.Workload!.Reader.Secret, cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3ReadOracle.VerifyFullAsync(reader, roots.Workload, expected, cancellationToken)
            .ConfigureAwait(false);
        await NodeEpochRf3AuthorizationOracle.VerifyRevocationAsync(wave.App, profile, reader,
            roots.Workload, cancellationToken).ConfigureAwait(false);
        var followed = await CommitWithFollowerStoppedAsync(wave, profile, roots.Workload, follower,
            Writer(beforeLoss, follower), cancellationToken).ConfigureAwait(false);
        var afterRestart = expected.Add(followed.Sample);
        await VerifyFollowerCatchupAsync(wave.App, profile, roots.Workload, afterRestart,
            roots.PriorNodes!, beforeLoss, follower, followed.RequiredApplied, reader, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<ImmutableArray<NodeEpochRf3ExpectedSample>> ApplyAndVerifyCurrentWritesAsync(
        DistributedApplication app, NodeEpochRf3Profile profile, NodeEpochRf3Workload workload,
        CancellationToken cancellationToken)
    {
        var retained = await NodeEpochRf3CurrentWorkload.ApplyRetentionAsync(app, profile, workload, cancellationToken)
            .ConfigureAwait(false);
        return await NodeEpochRf3CurrentWorkload.AppendCurrentAsync(app, profile, workload, retained,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<NodeEpochRf3FollowerWrite> CommitWithFollowerStoppedAsync(
        NodeEpochRf3Wave wave, NodeEpochRf3Profile profile, NodeEpochRf3Workload workload,
        string follower, string writer, CancellationToken cancellationToken)
    {
        await wave.KillAsync(follower, FollowerLossScenario, cancellationToken).ConfigureAwait(false);
        using var http = McpCallerHttp.Create(wave.App, writer);
        http.Timeout = Timeout.InfiniteTimeSpan;
        var client = new KeyLoadClient(http, profile.AdminKey);
        var expected = await NodeEpochRf3CurrentWorkload.AppendWhileFollowerStoppedAsync(workload,
            client, cancellationToken).ConfigureAwait(false);
        var status = await McpCallerAssertions.SdkSuccessAsync(await client.StatusAsync(cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await wave.RestartAsync(follower, cancellationToken).ConfigureAwait(false);
        return new(expected, status.Applied);
    }

    private static async Task VerifyFollowerCatchupAsync(DistributedApplication app, NodeEpochRf3Profile profile,
        NodeEpochRf3Workload workload, ImmutableArray<NodeEpochRf3ExpectedSample> expected,
        NodeEpochRf3NodeObservation[] originalNodes, NodeEpochRf3NodeObservation[] beforeLoss,
        string follower, long requiredApplied, NodeEpochRf3Callers revokedReader,
        CancellationToken cancellationToken)
    {
        await NodeEpochRf3StatusOracle.EventuallyCaughtUpAsync(app, profile, requiredApplied, cancellationToken)
            .ConfigureAwait(false);
        var healed = await NodeEpochRf3StatusOracle.CaptureAsync(app, profile, cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3StatusOracle.AssertSamePhysicalTopologyAsync(originalNodes, healed).ConfigureAwait(false);
        foreach (var node in healed)
        { await Assert.That(node.Status.Applied).IsGreaterThanOrEqualTo(requiredApplied); }
        var survivor = Writer(beforeLoss, follower);
        await using var callers = await NodeEpochRf3Callers.ConnectAsync(app, follower, survivor,
            profile.AdminKey, cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3ReadOracle.VerifyFullAsync(callers, workload, expected, cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3ReadOracle.VerifyRetentionStatusAsync(callers, workload,
            NodeEpochRf3Protocol.SampleStart.AddMinutes(10), 10, false, cancellationToken).ConfigureAwait(false);
        await NodeEpochRf3AuthorizationOracle.AssertDeniedAsync(revokedReader, workload, cancellationToken)
            .ConfigureAwait(false);
    }

    private static NodeEpochRf3NodeObservation Follower(IReadOnlyList<NodeEpochRf3NodeObservation> nodes)
    {
        var leaders = nodes.Select(node => node.Status.Leader).Where(value => value is not null)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (leaders.Length != 1)
        { throw new InvalidOperationException("The healthy RF3 topology did not report one actual leader."); }
        return nodes.First(node => node.Admin.LocalVoter != leaders[0]);
    }

    private static string Writer(IReadOnlyList<NodeEpochRf3NodeObservation> nodes, string follower)
        => nodes.First(node => node.Name != follower).Name;
}
