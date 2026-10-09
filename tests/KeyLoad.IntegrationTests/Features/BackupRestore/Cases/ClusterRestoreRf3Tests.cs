using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

[NotInParallel]
internal sealed class ClusterRestoreRf3Tests
{
    private const string RootPrefix = "cluster-restore-whole-";
    private const string EvidenceDirectory = "qualification";
    private const string ArtifactsDirectory = "artifacts";
    private const string IdentityFormat = "N";

    [Test]
    public Task ActualTwoRf3ArchiveRestoresAllRegisteredOwnersAndLinkedModelsWithFencedOriginalReceiptsAndColdContinuation()
        => RunAsync(ClusterRestoreRf3Scenario.RunAsync);

    [Test]
    public Task ActualSplitMovementVectorRefusesPublicationThenFreshCaptureRestoresMovedLinkedModelsAndColdContinuation()
        => RunAsync(ClusterRestoreRf3AuthorityTrialVector.RunAsync);

    [Test]
    public Task ActualCapturedRevokedAndExpiredCredentialsRefuseOfflinePublicationAndRestoredRequestsThenValidCredentialContinuesCold()
        => RunAsync(ClusterRestoreRf3CredentialTrial.RunAsync);

    internal static async Task RunAsync(Func<TwoRf3MembershipWave, PartitionMovementPublicParentRf3Seed,
        string, CancellationToken, Task> scenario)
    {
        using var deadline = new CancellationTokenSource(ClusterRestoreRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken,
            deadline.Token);
        var failures = new List<Exception>();
        TwoRf3MembershipWave? source = null;
        PartitionMovementPublicParentRf3Seed? seed = null;
        var ownedRoot = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName, ArtifactsDirectory,
            EvidenceDirectory, RootPrefix + Guid.NewGuid().ToString(IdentityFormat));
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (Directory.Exists(ownedRoot) || File.Exists(ownedRoot))
            { throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid); }
            Directory.CreateDirectory(ownedRoot);
            if (!OperatingSystem.IsWindows())
            { File.SetUnixFileMode(ownedRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
            source = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(caller.Token).ConfigureAwait(false);
            seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(source, caller.Token).ConfigureAwait(false);
            await scenario(source, seed, ownedRoot, caller.Token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (seed is { } calls)
        { await ServerFailureObserver.ObserveAsync(() => calls.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (source is { } topology)
        { await ServerFailureObserver.ObserveAsync(() => topology.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (failures.Count == 0 && Directory.Exists(ownedRoot))
        { ServerFailureObserver.Observe(() => Directory.Delete(ownedRoot, recursive: true), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
