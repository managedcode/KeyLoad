using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NodeEpochRf3ColdScenario
{
    private const string RetainedRootKey = "KeyLoad.NodeEpochRf3.TrialRoot";

    internal static async Task RunAsync(CancellationToken cancellationToken)
    {
        var roots = CreateRoots();
        NodeEpochRf3Wave? priorWave = null;
        NodeEpochRf3Wave? currentWave = null;
        var primaryFailures = new List<Exception>();
        var cleanupFailures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(() => RunPriorWaveAsync(roots, primaryFailures,
                value => priorWave = value, cancellationToken), primaryFailures).ConfigureAwait(false);
            if (primaryFailures.Count == 0)
            {
                await ServerFailureObserver.ObserveAsync(() => NodeEpochRf3CurrentWaveRunner.RunAsync(roots,
                    primaryFailures, value => currentWave = value, cancellationToken), primaryFailures).ConfigureAwait(false);
            }
        }
        finally
        {
            await SettleWaveAsync(currentWave, cleanupFailures).ConfigureAwait(false);
            await SettleWaveAsync(priorWave, cleanupFailures).ConfigureAwait(false);
            if (primaryFailures.Count == 0 && cleanupFailures.Count == 0)
            { ServerFailureObserver.Observe(() => DeleteSettledRoot(roots.Root), cleanupFailures); }
        }
        ThrowFailures(primaryFailures, cleanupFailures, roots.Root);
    }

    private static async Task RunPriorWaveAsync(NodeEpochRf3TrialRoots roots, List<Exception> failures,
        Action<NodeEpochRf3Wave> retainWave, CancellationToken cancellationToken)
    {
        var profileSeed = await NodeEpochRf3Profile.CreatePriorAsync(roots.Prior, cancellationToken).ConfigureAwait(false);
        var priorReference = await NodeEpochRf3ImageProof.ReadPriorReferenceAsync(cancellationToken).ConfigureAwait(false);
        await using var wave = await NodeEpochRf3Wave.StartAsync(roots.Prior, priorReference, cancellationToken).ConfigureAwait(false);
        retainWave(wave);
        await ServerFailureObserver.ObserveAsync(() => RunPriorOwnedAsync(roots, profileSeed, wave,
            cancellationToken), failures).ConfigureAwait(false);
    }

    private static async Task RunPriorOwnedAsync(NodeEpochRf3TrialRoots roots,
        (NodeEpochRf3Profile Profile, byte[] Bytes) profileSeed, NodeEpochRf3Wave wave,
        CancellationToken cancellationToken)
    {
        var workload = await NodeEpochRf3Workload.ConfigureAsync(wave.App, profileSeed.Profile.AdminKey,
            cancellationToken).ConfigureAwait(false);
        var nodes = await NodeEpochRf3StatusOracle.CaptureAsync(wave.App, profileSeed.Profile, cancellationToken)
            .ConfigureAwait(false);
        await using (var callers = await NodeEpochRf3Callers.ConnectAsync(wave.App,
            NodeEpochRf3Protocol.Node1, NodeEpochRf3Protocol.Node2, workload.Reader.Secret, cancellationToken)
            .ConfigureAwait(false))
        {
            await NodeEpochRf3ReadOracle.VerifyFullAsync(callers, workload,
                NodeEpochRf3ReadOracle.Prior(workload), cancellationToken).ConfigureAwait(false);
        }
        await wave.StopAsync(cancellationToken).ConfigureAwait(false);
        var savedProfile = await NodeEpochRf3Profile.ReadAsync(Path.Combine(roots.Prior,
            NodeEpochRf3Protocol.ProfileFile), cancellationToken).ConfigureAwait(false);
        if (!savedProfile.Bytes.AsSpan().SequenceEqual(profileSeed.Bytes))
        { throw new IOException("The stopped prior-wave profile differs from its exact private seed bytes."); }
        roots.Workload = workload;
        roots.Profile = savedProfile.Profile;
        roots.ProfileBytes = savedProfile.Bytes;
        roots.ProfileSha256 = savedProfile.Sha256;
        roots.PriorNodes = nodes;
    }

    private static async Task SettleWaveAsync(NodeEpochRf3Wave? wave, List<Exception> failures)
    {
        if (wave is null)
        { return; }
        using var deadline = new CancellationTokenSource(NodeEpochRf3Protocol.CleanupDeadline);
        await ServerFailureObserver.ObserveAsync(() => wave.RestartPendingAsync(deadline.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => wave.StopAsync(deadline.Token), failures).ConfigureAwait(false);
    }

    private static NodeEpochRf3TrialRoots CreateRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), "keyload-node-epoch-rf3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
        return new(root, Path.Combine(root, NodeEpochRf3Protocol.PriorDirectory),
            Path.Combine(root, NodeEpochRf3Protocol.CurrentDirectory),
            Path.Combine(root, NodeEpochRf3Protocol.NegativeDirectory));
    }

    private static void DeleteSettledRoot(string root)
    {
        if (Directory.Exists(root))
        { Directory.Delete(root, recursive: true); }
    }

    private static void ThrowFailures(List<Exception> primaryFailures, List<Exception> cleanupFailures, string root)
    {
        foreach (var failure in primaryFailures)
        { failure.Data[RetainedRootKey] = root; }
        var all = new List<Exception>(primaryFailures);
        if (primaryFailures.Count == 0 && cleanupFailures.Count > 0)
        { all.Add(new IOException("The cold-RF3 trial root was retained: " + root)); }
        all.AddRange(cleanupFailures);
        ServerFailureObserver.ThrowIfAny(all);
    }
}

internal sealed record NodeEpochRf3FollowerWrite(NodeEpochRf3ExpectedSample Sample, long RequiredApplied);

internal sealed class NodeEpochRf3TrialRoots(string root, string prior, string current, string negative)
{
    internal string Root { get; } = root;
    internal string Prior { get; } = prior;
    internal string Current { get; } = current;
    internal string Negative { get; } = negative;
    internal NodeEpochRf3Profile? Profile { get; set; }
    internal byte[]? ProfileBytes { get; set; }
    internal string? ProfileSha256 { get; set; }
    internal NodeEpochRf3Workload? Workload { get; set; }
    internal NodeEpochRf3NodeObservation[]? PriorNodes { get; set; }
}
