using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3Epoch7Scenario
{
    private const string RetainedRootKey = "KeyLoad.RequestCqrsRf3.Epoch7Root";
    private const string DataDirectory = "data";
    private const string MixedDirectory = "mixed";
    private const string CurrentDirectory = "current";
    private const string NegativeDirectory = "negative";
    private const int Native6SourceEpoch = 6;

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        var root = CreatePrivateRoot();
        var roots = new NodeEpochRf3TrialRoots(root, Path.Combine(root, DataDirectory),
            Path.Combine(root, CurrentDirectory), Path.Combine(root, NegativeDirectory));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => ExecuteAsync(roots, cancellationToken), failures)
            .ConfigureAwait(false);
        if (failures.Count == 0)
        { ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures); }
        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            { failure.Data[RetainedRootKey] = root; }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    private static async Task ExecuteAsync(NodeEpochRf3TrialRoots roots, CancellationToken cancellationToken)
    {
        var images = await RequestCqrsRf3ImageProof.ReadAsync(cancellationToken).ConfigureAwait(false);
        var seed = await NodeEpochRf3Profile.CreatePriorAsync(roots.Prior, cancellationToken).ConfigureAwait(false);
        var workload = await SeedNative6Async(roots.Prior, images.Rpc1, seed.Profile, cancellationToken)
            .ConfigureAwait(false);
        roots.Profile = seed.Profile;
        roots.ProfileBytes = seed.Bytes;
        roots.ProfileSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(seed.Bytes));
        roots.PriorNodes = workload.Nodes;
        var migration = await UpgradeNative6Async(roots, cancellationToken).ConfigureAwait(false);
        await VerifyMixedRejectionAsync(roots, images, seed.Profile, workload.Workload, cancellationToken)
            .ConfigureAwait(false);
        var restart = await ServeCurrentAsync(roots, images.Current, seed.Profile,
            workload.Workload, migration, cancellationToken).ConfigureAwait(false);
        await VerifyAfterRestartAsync(roots, images.Current, seed.Profile, workload.Workload,
            restart, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(RequestCqrsRf3Workload Workload, NodeEpochRf3NodeObservation[] Nodes)> SeedNative6Async(
        string dataRoot, string rpc1Image, NodeEpochRf3Profile profile, CancellationToken cancellationToken)
    {
        RequestCqrsRf3Workload? workload = null;
        NodeEpochRf3NodeObservation[]? nodes = null;
        await RequestCqrsRf3Epoch7WaveRunner.RunAsync(dataRoot, All(rpc1Image), true, true, async wave =>
        {
            workload = await RequestCqrsRf3Workload.SeedAsync(wave.App, profile, cancellationToken)
                .ConfigureAwait(false);
            nodes = await NodeEpochRf3StatusOracle.CaptureAsync(wave.App, profile, cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        return (workload ?? throw new InvalidOperationException("The original C1 workload was not seeded."),
            nodes ?? throw new InvalidOperationException("The native6 node observations were not captured."));
    }

    private static async Task<NodeEpochRf3Migration> UpgradeNative6Async(NodeEpochRf3TrialRoots roots,
        CancellationToken cancellationToken)
    {
        var migration = new NodeEpochRf3Migration(roots.Prior, roots.Current, roots.Profile!, roots.PriorNodes!,
            Native6SourceEpoch);
        await NodeEpochRf3CurrentWaveRunner.PrepareTargetsAsync(roots, migration, cancellationToken)
            .ConfigureAwait(false);
        await migration.VerifyOriginalProfileAsync(roots.ProfileBytes!, roots.ProfileSha256!, cancellationToken)
            .ConfigureAwait(false);
        return migration;
    }

    private static async Task VerifyMixedRejectionAsync(NodeEpochRf3TrialRoots roots,
        RequestCqrsRf3Images images, NodeEpochRf3Profile profile, RequestCqrsRf3Workload workload,
        CancellationToken cancellationToken)
    {
        var mixedRoot = Path.Combine(roots.Root, MixedDirectory);
        var originals = await RequestCqrsRf3Epoch7MixedRoot.CreateAsync(roots, mixedRoot, cancellationToken)
            .ConfigureAwait(false);
        await RequestCqrsRf3Epoch7WaveRunner.RunAsync(mixedRoot, Mixed(images), true, false,
            wave => RequestCqrsRf3MixedOracle.VerifyAsync(wave.App, profile, workload, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        await RequestCqrsRf3Epoch7MixedRoot.AssertUnchangedAsync(roots, mixedRoot, originals,
            roots.ProfileBytes!, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(NodeEpochRf3NodeObservation[] Nodes,
        KeyLoad.Orleans.ReplicaSiloDiscovery[] Discovery, CommitReceipt Receipt)> ServeCurrentAsync(
        NodeEpochRf3TrialRoots roots, string currentImage, NodeEpochRf3Profile profile,
        RequestCqrsRf3Workload workload, NodeEpochRf3Migration migration, CancellationToken cancellationToken)
    {
        NodeEpochRf3NodeObservation[]? nodes = null;
        KeyLoad.Orleans.ReplicaSiloDiscovery[]? discovery = null;
        CommitReceipt? receipt = null;
        await RequestCqrsRf3Epoch7WaveRunner.RunAsync(roots.Current, All(currentImage), false, true, async wave =>
        {
            nodes = await NodeEpochRf3StatusOracle.CaptureAsync(wave.App, profile, cancellationToken)
                .ConfigureAwait(false);
            await NodeEpochRf3StatusOracle.AssertSamePhysicalTopologyAsync(roots.PriorNodes!, nodes)
                .ConfigureAwait(false);
            await workload.VerifyPreservedAsync(wave.App, profile, cancellationToken).ConfigureAwait(false);
            discovery = await RequestCqrsRf3DiscoveryOracle.CaptureCurrentAsync(wave.App, profile,
                cancellationToken).ConfigureAwait(false);
            receipt = await workload.AppendCurrentWriteAsync(wave.App, profile, RequestCqrsRf3Protocol.Node1,
                cancellationToken).ConfigureAwait(false);
            await workload.VerifyCurrentWriteAsync(wave.App, profile, RequestCqrsRf3Protocol.Node1,
                receipt ?? throw new InvalidOperationException("The native7 write receipt is missing."),
                cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        await migration.VerifyPublishedRetryPreservesLaterWritesAsync(cancellationToken).ConfigureAwait(false);
        await migration.VerifyOriginalProfileAsync(roots.ProfileBytes!, roots.ProfileSha256!, cancellationToken)
            .ConfigureAwait(false);
        return (nodes!, discovery!, receipt!);
    }

    private static async Task VerifyAfterRestartAsync(NodeEpochRf3TrialRoots roots, string currentImage,
        NodeEpochRf3Profile profile, RequestCqrsRf3Workload workload,
        (NodeEpochRf3NodeObservation[] Nodes, KeyLoad.Orleans.ReplicaSiloDiscovery[] Discovery,
            CommitReceipt Receipt) prior, CancellationToken cancellationToken)
    {
        await RequestCqrsRf3Epoch7WaveRunner.RunAsync(roots.Current, All(currentImage), false, true, async wave =>
        {
            await RequestCqrsRf3DiscoveryOracle.AssertReplacementAsync(prior.Discovery,
                await RequestCqrsRf3DiscoveryOracle.CaptureCurrentAsync(wave.App, profile, cancellationToken)
                    .ConfigureAwait(false)).ConfigureAwait(false);
            await NodeEpochRf3StatusOracle.EventuallyCaughtUpAsync(wave.App, profile,
                prior.Receipt.Token.Position, cancellationToken).ConfigureAwait(false);
            var current = await NodeEpochRf3StatusOracle.CaptureAsync(wave.App, profile, cancellationToken)
                .ConfigureAwait(false);
            await NodeEpochRf3StatusOracle.AssertSamePhysicalTopologyAsync(prior.Nodes, current).ConfigureAwait(false);
            foreach (var node in current)
            { await Assert.That(node.Status.Applied).IsGreaterThanOrEqualTo(prior.Receipt.Token.Position); }
            await workload.VerifyPreservedAsync(wave.App, profile, cancellationToken, currentWriteExists: true)
                .ConfigureAwait(false);
            await workload.VerifyCurrentWriteAsync(wave.App, profile, RequestCqrsRf3Protocol.Node2,
                prior.Receipt, cancellationToken).ConfigureAwait(false);
            await VerifyProfileAsync(roots.Current, roots.ProfileBytes!, profile, cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<string, string> All(string reference)
        => new(StringComparer.Ordinal)
        {
            [RequestCqrsRf3Protocol.Node1] = reference,
            [RequestCqrsRf3Protocol.Node2] = reference,
            [RequestCqrsRf3Protocol.Node3] = reference
        };

    private static async Task VerifyProfileAsync(string dataRoot, byte[] expected,
        NodeEpochRf3Profile profile, CancellationToken cancellationToken)
    {
        var actual = await NodeEpochRf3Profile.ReadAsync(Path.Combine(dataRoot,
            NodeEpochRf3Protocol.ProfileFile), cancellationToken).ConfigureAwait(false);
        await Assert.That(actual.Bytes.AsSpan().SequenceEqual(expected)).IsTrue();
        await Assert.That(actual.Profile.Incarnation).IsEqualTo(profile.Incarnation);
    }

    private static Dictionary<string, string> Mixed(RequestCqrsRf3Images images)
        => new(StringComparer.Ordinal)
        {
            [RequestCqrsRf3Protocol.Node1] = images.Current,
            [RequestCqrsRf3Protocol.Node2] = images.Rpc1,
            [RequestCqrsRf3Protocol.Node3] = images.Rpc1
        };

    private static string CreatePrivateRoot()
    {
        var artifacts = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            "artifacts", "qualification");
        Directory.CreateDirectory(artifacts);
        var path = Path.Combine(artifacts, "cluster-routing-c1-epoch7-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        return path;
    }
}
